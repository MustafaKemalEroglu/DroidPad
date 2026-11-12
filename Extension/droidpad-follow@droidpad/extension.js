import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

// DroidPad kaplaması Waydroid'i takip eder:
//  - Kaplama Waydroid penceresinin konum/boyutunu birebir izler.
//  - Waydroid hangi masaüstündeyse (taşınırsa) DroidPad de oraya gider.
//  - Waydroid ön plandayken DroidPad onun üstüne çıkar; başka bir uygulama
//    ön plana geçince o uygulamanın arkasında kalır (always-on-top yok).
export default class DroidPadFollow extends Extension {
    enable() {
        this._tracked = new Map(); // Meta.Window -> sinyal kimlikleri
        this._syncId = 0;
        this._padSeen = false;

        this._displayIds = [
            global.display.connect('notify::focus-window', () => this._sync(false)),
            global.display.connect('window-created', (_d, w) => this._track(w)),
        ];
        for (const a of global.get_window_actors())
            this._track(a.meta_window);
    }

    disable() {
        for (const id of this._displayIds) global.display.disconnect(id);
        this._displayIds = [];
        for (const [w, ids] of this._tracked) for (const id of ids) w.disconnect(id);
        this._tracked.clear();
        if (this._syncId) GLib.source_remove(this._syncId);
        this._syncId = 0;
    }

    _cls(w) {
        return `${w.get_wm_class() ?? ''} ${w.get_gtk_application_id() ?? ''}`.toLowerCase();
    }

    _isPad(w) {
        return this._cls(w).includes('droidpad') || w.get_title() === 'DroidPad';
    }

    _isWaydroid(w) {
        return !this._isPad(w) && this._cls(w).includes('waydroid');
    }

    _track(w) {
        if (this._tracked.has(w) || w.get_window_type() !== Meta.WindowType.NORMAL) return;
        const ids = [
            w.connect('workspace-changed', () => { if (this._isWaydroid(w)) this._sync(true); }),
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

    _fitIfWaydroid(w) {
        if (!this._isWaydroid(w)) return;
        const {pad, waydroid} = this._windows();
        if (pad && waydroid === w && pad.get_workspace() === w.get_workspace()) this._fit(pad, w);
    }

    // Kaplamayı Waydroid penceresinin konum ve boyutuna eşitler.
    _fit(pad, wd) {
        if (wd.is_fullscreen()) {
            if (!pad.is_fullscreen()) pad.make_fullscreen();
            return;
        }
        if (pad.is_fullscreen()) pad.unmake_fullscreen();
        if (pad.get_maximized()) {
            try { pad.unmaximize(Meta.MaximizeFlags.BOTH); } catch (_e) { pad.unmaximize(); }
        }
        const r = wd.get_frame_rect();
        const c = pad.get_frame_rect();
        if (r.x !== c.x || r.y !== c.y || r.width !== c.width || r.height !== c.height)
            pad.move_resize_frame(false, r.x, r.y, r.width, r.height);
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

        // Yalnızca Waydroid (veya kaplamanın kendisi) odaktayken öne çık.
        if (firstTime || follow || focus === waydroid || focus === pad)
            pad.raise();
    }
}
