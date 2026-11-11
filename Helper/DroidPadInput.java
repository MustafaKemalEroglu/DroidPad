import android.os.SystemClock;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.view.MotionEvent.PointerCoords;
import android.view.MotionEvent.PointerProperties;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.lang.reflect.Method;

/**
 * DroidPad cihaz tarafı girdi yardımcısı (scrcpy yöntemi): "app_process" ile kalıcı çalışır,
 * stdin'den satır komutları okur ve gerçek çok işaretçili MotionEvent enjekte eder.
 * Her olay için yeni "input" süreci (JVM) başlatmak gerekmez.
 *
 * Komutlar (boşlukla ayrık):
 *   d ID X Y   işaretçi bas        m ID X Y   işaretçi taşı      u ID   işaretçi bırak
 *   t ID X Y   kısa dokunuş (engellemez)       r   hepsini bırak
 *   p ID X1 Y1 X2 Y2 MS   MS sürede kaydırma (X1,Y1=X2,Y2 ise uzun basma; engellemez)
 *   s          "@@" yaz (eşitleme)           q   çık
 */
public final class DroidPadInput {
    private static final int MAX = 10;
    private static Object inputManager;
    private static Method inject;

    private static final int[] ids = new int[MAX];
    private static final float[] xs = new float[MAX];
    private static final float[] ys = new float[MAX];
    private static int count;
    private static long downTime;

    public static void main(String[] args) throws Exception {
        Class<?> imClass = Class.forName("android.hardware.input.InputManager");
        inputManager = imClass.getDeclaredMethod("getInstance").invoke(null);
        inject = imClass.getMethod("injectInputEvent", android.view.InputEvent.class, int.class);

        BufferedReader in = new BufferedReader(new InputStreamReader(System.in));
        String line;
        while ((line = in.readLine()) != null) {
            try {
                if (!handle(line.trim())) break;
            } catch (Throwable t) {
                System.err.println("hata: " + line + " -> " + t);
            }
        }
        releaseAll();
    }

    private static boolean handle(String line) {
        if (line.isEmpty()) return true;
        String[] p = line.split(" ");
        switch (p[0]) {
            case "d": down(Integer.parseInt(p[1]), f(p[2]), f(p[3])); break;
            case "m": move(Integer.parseInt(p[1]), f(p[2]), f(p[3])); break;
            case "u": up(Integer.parseInt(p[1])); break;
            case "t":
                gesture(Integer.parseInt(p[1]), f(p[2]), f(p[3]), f(p[2]), f(p[3]), 20);
                break;
            case "p":
                gesture(Integer.parseInt(p[1]), f(p[2]), f(p[3]), f(p[4]), f(p[5]), Integer.parseInt(p[6]));
                break;
            case "r": releaseAll(); break;
            case "s": System.out.println("@@"); System.out.flush(); break;
            case "q": return false;
            default: break;
        }
        return true;
    }

    /** Ayrı iş parçacığında: bas → (ara noktalar) → bırak; okuma döngüsü beklemez. */
    private static void gesture(final int id, final float x1, final float y1,
                                final float x2, final float y2, final int ms) {
        Thread t = new Thread(new Runnable() {
            public void run() {
                down(id, x1, y1);
                int steps = (x1 == x2 && y1 == y2) ? 1 : Math.max(1, ms / 16);
                for (int i = 1; i <= steps; i++) {
                    SystemClock.sleep(Math.max(1, ms / steps));
                    if (x1 != x2 || y1 != y2) {
                        float k = i / (float) steps;
                        move(id, x1 + (x2 - x1) * k, y1 + (y2 - y1) * k);
                    }
                }
                up(id);
            }
        });
        t.setDaemon(true);
        t.start();
    }

    private static float f(String s) { return Float.parseFloat(s); }

    private static int indexOf(int id) {
        for (int i = 0; i < count; i++) if (ids[i] == id) return i;
        return -1;
    }

    private static synchronized void down(int id, float x, float y) {
        int i = indexOf(id);
        if (i >= 0) { move(id, x, y); return; }
        if (count >= MAX) return;
        ids[count] = id; xs[count] = x; ys[count] = y;
        count++;
        if (count == 1) {
            downTime = SystemClock.uptimeMillis();
            dispatch(MotionEvent.ACTION_DOWN);
        } else {
            dispatch(MotionEvent.ACTION_POINTER_DOWN | ((count - 1) << MotionEvent.ACTION_POINTER_INDEX_SHIFT));
        }
    }

    private static synchronized void move(int id, float x, float y) {
        int i = indexOf(id);
        if (i < 0) return;
        xs[i] = x; ys[i] = y;
        dispatch(MotionEvent.ACTION_MOVE);
    }

    private static synchronized void up(int id) {
        int i = indexOf(id);
        if (i < 0) return;
        if (count == 1) {
            dispatch(MotionEvent.ACTION_UP);
        } else {
            dispatch(MotionEvent.ACTION_POINTER_UP | (i << MotionEvent.ACTION_POINTER_INDEX_SHIFT));
        }
        for (int j = i; j < count - 1; j++) { ids[j] = ids[j + 1]; xs[j] = xs[j + 1]; ys[j] = ys[j + 1]; }
        count--;
    }

    private static synchronized void releaseAll() {
        while (count > 0) up(ids[count - 1]);
    }

    private static synchronized void dispatch(int action) {
        PointerProperties[] props = new PointerProperties[count];
        PointerCoords[] coords = new PointerCoords[count];
        for (int i = 0; i < count; i++) {
            props[i] = new PointerProperties();
            props[i].id = ids[i];
            props[i].toolType = MotionEvent.TOOL_TYPE_FINGER;
            coords[i] = new PointerCoords();
            coords[i].x = xs[i];
            coords[i].y = ys[i];
            coords[i].pressure = 1f;
            coords[i].size = 1f;
        }
        long now = SystemClock.uptimeMillis();
        MotionEvent ev = MotionEvent.obtain(downTime, now, action, count, props, coords,
                0, 0, 1f, 1f, 0, 0, InputDevice.SOURCE_TOUCHSCREEN, 0);
        try {
            inject.invoke(inputManager, ev, 0); // INJECT_INPUT_EVENT_MODE_ASYNC
        } catch (Throwable t) {
            System.err.println("enjeksiyon hatası: " + t);
        } finally {
            ev.recycle();
        }
    }
}
