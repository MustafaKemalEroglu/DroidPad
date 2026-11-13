import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';

// DroidPad kaplaması Waydroid'i takip eder:
//  - Kaplama Waydroid penceresinin konum/boyutunu birebir izler.
//  - Waydroid hangi masaüstündeyse (taşınırsa) DroidPad de oraya gider.
//  - Waydroid ön plandayken DroidPad onun üstüne çıkar; başka bir uygulama
//    ön plana geçince o uygulamanın arkasında kalır (always-on-top yok).
// DroidPad, metin kutusu açıkken klavyeyi Waydroid'e doğrudan vermek için bu D-Bus arayüzünü çağırır:
// Waydroid penceresi odaklanır (klavye düzeni/Türkçe karakterler Android'e yerel olarak gider),
// kaplama yine üstte durur. Yazma bitince FocusPad ile odak kaplamaya döner.
const DBUS_XML = `<node><interface name="org.droidpad.Follow">
  <method name="FocusWaydroid"/><method name="FocusPad"/>
</interface></node>`;

export class Follow {
    enable() {
        this._tracked = new Map(); // Meta.Window -> sinyal kimlikleri
        this._syncId = 0;
        this._padSeen = false;
        this._wdWs = null; // Waydroid'in son bilinen masaüstü
        this._prev = null; // kaplama dışında en son odaklanan pencere

        this._displayIds = [
            global.display.connect('notify::focus-window', () => this._sync(false)),
            global.display.connect('window-created', (_d, w) => this._track(w)),
        ];
        this._wsId = global.workspace_manager.connect('workspace-switched', () => this._queue(false));
        for (const a of global.get_window_actors())
            this._track(a.meta_window);

        try {
            this._dbus = Gio.DBusExportedObject.wrapJSObject(DBUS_XML, this);
            this._dbus.export(Gio.DBus.session, '/org/droidpad/Follow');
            this._nameId = Gio.bus_own_name_on_connection(Gio.DBus.session, 'org.droidpad.Follow',
                Gio.BusNameOwnerFlags.NONE, null, null);
        } catch (e) {
            console.error(`DroidPadFollow: D-Bus arayüzü açılamadı: ${e}`);
        }
    }

    FocusWaydroid() {
        const {waydroid} = this._windows();
        if (waydroid) waydroid.activate(global.get_current_time());
    }

    FocusPad() {
        const {pad} = this._windows();
        if (pad) pad.activate(global.get_current_time());
    }

    disable() {
        if (this._nameId) Gio.bus_unown_name(this._nameId);
        this._nameId = 0;
        if (this._dbus) this._dbus.unexport();
        this._dbus = null;
        for (const id of this._displayIds) global.display.disconnect(id);
        this._displayIds = [];
        if (this._wsId) global.workspace_manager.disconnect(this._wsId);
        this._wsId = 0;
        if (this._raiseId) GLib.source_remove(this._raiseId);
        this._raiseId = 0;
        for (const [w, ids] of this._tracked) for (const id of ids) w.disconnect(id);
        this._tracked.clear();
        if (this._syncId) GLib.source_remove(this._syncId);
        this._syncId = 0;
    }

    _cls(w) {
        return `${w.get_wm_class() ?? ''} ${w.get_gtk_application_id() ?? ''}`.toLowerCase();
    }

    _isPad(w) {
        // Başlığa bakılmaz: "DroidPad" adlı bir klasör (Dosyalar penceresi) de aynı başlığı taşır.
        // Başlık yalnızca pencerenin sınıfı hiç bildirilmediyse yedek olarak kullanılır.
        const cls = this._cls(w).trim();
        return cls.includes('droidpad') || (cls === '' && w.get_title() === 'DroidPad');
    }

    _isWaydroid(w) {
        return !this._isPad(w) && this._cls(w).includes('waydroid');
    }

    _track(w) {
        if (this._tracked.has(w) || w.get_window_type() !== Meta.WindowType.NORMAL) return;
        const ids = [
            w.connect('workspace-changed', () => this._onWorkspaceChanged(w)),
            w.connect('position-changed', () => this._fitIfWaydroid(w)),
            w.connect('size-changed', () => this._fitIfWaydroid(w)),
            w.connect('notify::title', () => this._queue(false)),
            w.connect('notify::wm-class', () => this._queue(false)),
            w.connect('unmanaged', () => {
                this._tracked.delete(w);
                if (this._isPad(w)) this._padSeen = false;
            }),
        ];
        this._tracked.set(w, ids);
        this._queue(false);
    }

    // Dinamik masaüstlerinde GNOME masaüstü eklenip silinince de 'workspace-changed' yayılabilir;
    // yalnızca Waydroid GERÇEKTEN başka masaüstüne taşındıysa o masaüstüne geçilir.
    _onWorkspaceChanged(w) {
        if (!this._isWaydroid(w)) return;
        const ws = w.get_workspace();
        const moved = this._wdWs !== undefined && this._wdWs !== null && ws !== this._wdWs;
        this._wdWs = ws;
        if (moved) this._sync(true);
        else this._queue(false);
    }

    _fitIfWaydroid(w) {
        if (!this._isWaydroid(w)) return;
        const {pad, waydroid} = this._windows();
        if (pad && waydroid === w && pad.get_workspace() === w.get_workspace()) this._fit(pad, w);
    }

    // Not: actor'ın dönüştürülmüş konumu masaüstü geçiş animasyonunda kaydığı için kullanılmaz; çerçeve esas alınır.
    _visualRect(w) {
        const f = w.get_frame_rect();
        return {x: f.x, y: f.y, width: f.width, height: f.height};
    }

    // GNOME 49+ get_maximized/MaximizeFlags'i kaldırdı (is_maximized/unmaximize()); eski sürümleri de destekle.
    _isMax(w) {
        return typeof w.is_maximized === 'function' ? w.is_maximized() : w.get_maximized() !== 0;
    }

    _unmax(w) {
        try { w.unmaximize(); } catch (_e) { w.unmaximize(Meta.MaximizeFlags.BOTH); }
    }

    _coversMonitor(w, r) {
        const m = global.display.get_monitor_geometry(w.get_monitor());
        return r.width >= m.width - 2 && r.height >= m.height - 2;
    }

    // Kaplamayı Waydroid penceresinin konum ve boyutuna eşitler.
    _fit(pad, wd) {
        const r = this._visualRect(wd);
        if (wd.is_fullscreen() || this._coversMonitor(wd, r)) {
            if (!pad.is_fullscreen()) pad.make_fullscreen();
            return;
        }
        if (pad.is_fullscreen()) pad.unmake_fullscreen();
        if (this._isMax(pad)) this._unmax(pad);
        const c = pad.get_frame_rect();
        if (r.x !== c.x || r.y !== c.y || r.width !== c.width || r.height !== c.height)
            pad.move_resize_frame(false, r.x, r.y, r.width, r.height);
    }

    _raiseLater(pad, waydroid) {
        if (this._raiseId) GLib.source_remove(this._raiseId);
        this._raiseId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 250, () => {
            this._raiseId = 0;
            if (global.display.focus_window === waydroid &&
                this._tracked.has(pad) && pad.get_workspace() === waydroid.get_workspace())
                pad.raise();
            return GLib.SOURCE_REMOVE;
        });
    }

    _queue(follow) {
        if (this._syncId) return;
        this._syncId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 200, () => {
            this._syncId = 0;
            this._sync(follow);
            return GLib.SOURCE_REMOVE;
        });
    }

    _windows() {
        let pad = null, waydroid = null;
        const focus = global.display.focus_window;
        for (const w of this._tracked.keys()) {
            if (this._isPad(w)) pad = w;
            else if (this._isWaydroid(w) && (!waydroid || w === focus)) waydroid = w;
        }
        return {pad, waydroid, focus};
    }

    // follow=true: Waydroid masaüstü değiştirdi → o masaüstüne geç.
    _sync(follow) {
        const {pad, waydroid, focus} = this._windows();
        if (!pad || !waydroid) return;

        const ws = waydroid.get_workspace();
        if (!ws) return;

        const firstTime = !this._padSeen;
        this._padSeen = true;

        if (pad.get_workspace() !== ws) pad.change_workspace(ws);
        if (pad.get_monitor() !== waydroid.get_monitor())
            pad.move_to_monitor(waydroid.get_monitor());
        if (pad.minimized) pad.unminimize();
        this._fit(pad, waydroid);

        if (follow && global.workspace_manager.get_active_workspace() !== ws)
            ws.activate(global.get_current_time());

        if (focus && focus !== pad) this._prev = focus;
        if (this._wdWs === undefined || this._wdWs === null) this._wdWs = ws;

        // Kullanıcı başka bir masaüstündeyken öne alma/odak işlemi yapılmaz; aksi halde masaüstü geçişine karışır.
        if (global.workspace_manager.get_active_workspace() !== ws) return;

        // Önceki pencere başka bir masaüstündeyse (ör. DroidPad 1. masaüstünde açılıp buraya taşındı) geçerli sayılmaz;
        // aksi halde odağı ona vermek kullanıcıyı o masaüstüne geri çekerdi.
        const prev = this._prev && this._tracked.has(this._prev) && this._prev.get_workspace() === ws
            ? this._prev : null;

        // Kaplama yalnızca Waydroid ön plandayken (veya ondan geçilen kaplamaya odaklanılmışsa) öne çıkar.
        const allowed = firstTime || follow || focus === waydroid ||
            (focus === pad && (prev === null || prev === waydroid));
        if (allowed) {
            pad.raise();
            // Masaüstü geçişinde Waydroid odağı/yığın sırasını bizden sonra güncelleyebilir; kısa süre sonra tekrar öne al.
            if (focus === waydroid) this._raiseLater(pad, waydroid);
        } else if (focus === pad) {
            // Aynı masaüstünde başka uygulama ön plandayken kaplamaya odaklanılsa bile öne çıkmasın:
            // arkaya at, odağı önceki uygulamaya geri ver (yalnızca etkin masaüstündeyse).
            pad.lower();
            if (prev && prev !== pad && global.workspace_manager.get_active_workspace() === ws)
                prev.activate(global.get_current_time());
        }
    }
}
