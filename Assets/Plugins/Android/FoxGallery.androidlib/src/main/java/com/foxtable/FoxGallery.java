package com.foxtable;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.os.Bundle;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.util.UUID;
import org.json.JSONObject;

/** Bounded file handoff: image bytes never cross UnitySendMessage/JNI as a Base64 string. */
public final class FoxGallery extends Activity {
    private static final int MAX_EDGE = 2048, MAX_BYTES = 6 * 1024 * 1024;
    private static String pendingResult = "";
    private static synchronized void result(String json) { pendingResult = json; }
    public static synchronized String takeResult() {
        String value = pendingResult; pendingResult = ""; return value;
    }
    public static String cacheDirectory(Activity owner) {
        return new File(owner.getCacheDir(), "foxtable-import").getAbsolutePath();
    }
    public static void open(Activity owner) {
        result("");
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
    @Override protected void onActivityResult(int request, int code, Intent data) {
        super.onActivityResult(request, code, data);
        if (request != 71 || code != RESULT_OK || data == null || data.getData() == null) {
            result("{\"cancelled\":true}"); finish(); return;
        }
        final android.net.Uri uri = data.getData();
        new Thread(() -> {
            File source = null, output = null;
            Bitmap bitmap = null;
            try {
                File directory = new File(cacheDirectory(this));
                if (!directory.exists() && !directory.mkdirs()) throw new Exception("No cache directory");
                // Remove abandoned imports, never campaign files.
                File[] abandoned = directory.listFiles();
                if (abandoned != null) for (File file : abandoned)
                    if (System.currentTimeMillis() - file.lastModified() > 24L*60*60*1000) file.delete();
                source = File.createTempFile("source-", ".tmp", directory);
                try (InputStream input = getContentResolver().openInputStream(uri);
                     FileOutputStream stream = new FileOutputStream(source)) {
                    if (input == null) throw new Exception("No stream");
                    byte[] chunk = new byte[16384]; int count; long total = 0;
                    while ((count = input.read(chunk)) != -1) {
                        total += count;
                        if (total > 32L*1024*1024) throw new IllegalArgumentException("Исходная картинка должна быть меньше 32 МБ");
                        stream.write(chunk, 0, count);
                    }
                }
                BitmapFactory.Options bounds = new BitmapFactory.Options();
                bounds.inJustDecodeBounds = true;
                BitmapFactory.decodeFile(source.getAbsolutePath(), bounds);
                if (bounds.outWidth <= 0 || bounds.outHeight <= 0 ||
                    (long)bounds.outWidth*bounds.outHeight > 100L*1024*1024 ||
                    !("image/png".equals(bounds.outMimeType) || "image/jpeg".equals(bounds.outMimeType)))
                    throw new IllegalArgumentException("Нужна корректная картинка PNG или JPEG до 100 мегапикселей");
                boolean png = "image/png".equals(bounds.outMimeType);
                String fileName = UUID.randomUUID().toString().replace("-", "") + (png ? ".png" : ".jpg");
                output = new File(directory, fileName);
                boolean resized = Math.max(bounds.outWidth, bounds.outHeight) > MAX_EDGE || source.length() > MAX_BYTES;
                if (!resized) {
                    if (!source.renameTo(output)) throw new Exception("Could not stage image");
                } else {
                    BitmapFactory.Options options = new BitmapFactory.Options();
                    options.inSampleSize = 1; options.inScaled = false;
                    // Decode directly to a bounded bitmap rather than first allocating the full original.
                    while ((bounds.outWidth + options.inSampleSize - 1) / options.inSampleSize > MAX_EDGE ||
                           (bounds.outHeight + options.inSampleSize - 1) / options.inSampleSize > MAX_EDGE)
                        options.inSampleSize *= 2;
                    options.inPreferredConfig = Bitmap.Config.ARGB_8888;
                    bitmap = BitmapFactory.decodeFile(source.getAbsolutePath(), options);
                    if (bitmap == null) throw new IllegalArgumentException("Не удалось декодировать картинку");
                    while (true) {
                        try (FileOutputStream stream = new FileOutputStream(output)) {
                            if (!bitmap.compress(png ? Bitmap.CompressFormat.PNG : Bitmap.CompressFormat.JPEG, 90, stream))
                                throw new Exception("Could not encode image");
                        }
                        if (output.length() <= MAX_BYTES) break;
                        Bitmap smaller = Bitmap.createScaledBitmap(bitmap, Math.max(1,bitmap.getWidth()/2), Math.max(1,bitmap.getHeight()/2), true);
                        bitmap.recycle(); bitmap = smaller;
                    }
                }
                JSONObject payload = new JSONObject();
                payload.put("fileName", fileName); payload.put("resized", resized);
                result(payload.toString());
                output = null; // Ownership transferred to Unity; deleted after reading.
            } catch (OutOfMemoryError ex) {
                error("Не хватило памяти для картинки. Закрой другие приложения или выбери меньший файл");
            } catch (IllegalArgumentException ex) {
                error(ex.getMessage());
            } catch (Exception ex) {
                error("Не удалось прочитать карту. Проверь файл и свободное место");
            } finally {
                if (bitmap != null) bitmap.recycle();
                if (source != null) source.delete();
                if (output != null) output.delete();
                runOnUiThread(this::finish);
            }
        }, "FoxTable-Picker").start();
    }
    private static void error(String message) {
        try { JSONObject obj = new JSONObject(); obj.put("error", message); result(obj.toString()); }
        catch (Exception ex) { result("{\"error\":\"Ошибка импорта картинки\"}"); }
    }
}
