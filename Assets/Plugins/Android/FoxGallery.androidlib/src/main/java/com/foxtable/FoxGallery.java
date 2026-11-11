package com.foxtable;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.util.Base64;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import org.json.JSONObject;

/** System document picker: no broad photo-library or storage permission. */
public final class FoxGallery extends Activity {
    public static void open(Activity owner) {
        owner.startActivity(new Intent(owner, FoxGallery.class));
    }

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        if (state == null) {
            Intent picker = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            picker.setType("image/*");
            picker.putExtra(Intent.EXTRA_MIME_TYPES, new String[]{"image/png", "image/jpeg"});
            picker.addCategory(Intent.CATEGORY_OPENABLE);
            try { startActivityForResult(picker, 71); }
            catch (Exception ex) { error("Не удалось открыть выбор файла"); finish(); }
        }
    }

    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != 71 || result != RESULT_OK || data == null || data.getData() == null) { finish(); return; }
        final android.net.Uri uri = data.getData();
        new Thread(() -> {
            try (InputStream input = getContentResolver().openInputStream(uri);
                 ByteArrayOutputStream bytes = new ByteArrayOutputStream()) {
                if (input == null) throw new Exception("No stream");
                byte[] chunk = new byte[8192];
                int n;
                while ((n = input.read(chunk)) != -1) {
                    if (bytes.size() + n > 6 * 1024 * 1024) {
                        error("Карта должна быть меньше 6 МБ"); return;
                    }
                    bytes.write(chunk, 0, n);
                }
                String mime = getContentResolver().getType(uri);
                JSONObject payload = new JSONObject();
                payload.put("mime", mime == null ? "image/png" : mime);
                payload.put("data", Base64.encodeToString(bytes.toByteArray(), Base64.NO_WRAP));
                send(payload.toString());
            } catch (Exception ex) { error("Не удалось прочитать карту"); }
            finally { runOnUiThread(this::finish); }
        }, "FoxTable-Picker").start();
    }

    private static void error(String message) {
        try { JSONObject obj = new JSONObject(); obj.put("error", message); send(obj.toString()); }
        catch (Exception ignored) { }
    }

    private static void send(String json) {
        try {
            Class.forName("com.unity3d.player.UnityPlayer")
                .getMethod("UnitySendMessage", String.class, String.class, String.class)
                .invoke(null, "FoxTable", "OnAndroidImage", json);
        } catch (Exception ignored) { }
    }
}
