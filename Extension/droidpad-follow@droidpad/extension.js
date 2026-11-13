import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

// İnce yükleyici: asıl mantık follow.js içindedir. Her enable()'da follow.js benzersiz adlı bir kopyaya alınıp
// içe aktarılır; böylece dosya değişince oturumu kapatmadan `gnome-extensions disable/enable` ile yeni kod yüklenir.
// (Bu dosyanın kendisi değişirse oturumu yeniden açmak gerekir.)
export default class DroidPadFollowLoader extends Extension {
    async enable() {
        const token = this._token = {};
        const src = Gio.File.new_for_path(`${this.path}/follow.js`);
        const copy = Gio.File.new_for_path(
            `${GLib.get_user_cache_dir()}/droidpad-follow-${Date.now()}.js`);
        try {
            src.copy(copy, Gio.FileCopyFlags.OVERWRITE, null, null);
            this._copy = copy;
            const mod = await import(copy.get_uri());
            if (this._token !== token) return; // bu arada disable() çağrıldı
            this._impl = new mod.Follow();
            this._impl.enable();
        } catch (e) {
            console.error(`DroidPadFollow: yüklenemedi: ${e}\n${e.stack ?? ''}`);
        }
    }

    disable() {
        this._token = null;
        try { this._impl?.disable(); } catch (e) { console.error(`DroidPadFollow: disable: ${e}`); }
        this._impl = null;
        try { this._copy?.delete(null); } catch (_e) { /* yoksay */ }
        this._copy = null;
    }
}
