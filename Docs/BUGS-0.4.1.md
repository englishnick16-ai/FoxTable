# FoxTable 0.4.1: import, touch and board cost

## Report and baseline

User report after APK 0.4.0 / build 10: general lag, crash when importing an image, difficult movement and resizing. No device log or source image was supplied. Device crash and Android frame times have not been reproduced here; no connected Android device or local Unity Editor is available. Baseline runtime commit: 89eb526906ed74106fee6640760529051179e463; baseline packaged artifact retained unchanged.

## Evidence and changes

- Android picker previously constructed a Base64 string (up to 8 MiB ASCII), a JSON string, a JNI message and another managed byte array. The preview, local upload validation and board each decoded the image to a separate readable/mipmapped texture. Texture destruction is deferred, so some allocations overlapped. These are confirmed code paths, not a confirmed stack trace of the user's crash.
- Picker now copies the source to a private temporary file with a 32 MiB input limit, reads bitmap bounds before decoding, samples to at most 2048 px per edge and bounds the resulting PNG/JPEG to 6 MiB. A small polled filename/result survives the activity pause; image data is never sent through UnitySendMessage. C# validates the cache filename, consumes it and deletes it. Source and failed output files are cleaned; abandoned imports expire after a day. PNG alpha is preserved.
- Map textures have no mip chain and do not retain a readable CPU copy. The upload step no longer decodes a second validation texture. Closing/cancelling the preview releases the pending image. Board loading yields a frame so preview destruction can finish first. Existing campaign files/formats and map assets are not deleted or migrated.
- Board Refresh previously rewrote positions/sizes/font sizes for every token, resized the map and invalidated grid/fog tessellation for every pointer event. A fully hidden 80x80 map could draw 6400 filled paths per repaint. New camera movement changes one parent transform; token drag changes only that token's transforms. Permanent geometry is retained. Fog uses one 80x80 RGBA texture at maximum board size (25,600 raw pixel bytes), updated on snapshots, not on camera gestures. This is a structural cost reduction, not a measured claim of Android FPS.
- Touch release uses the last tracked position. After a pinch, the remaining finger immediately continues panning. Interrupted drags restore the authoritative position; completed drags retain their local preview until a newer snapshot, avoiding the old snap-back while the command is pending. Server authority is retained.
- Separate visible modes "Фигуры" and "Карта" reduce accidental token dragging. Selected figures have direct size and rotation buttons. Image/grid preview supports drag, pinch, explicit +/- and fit; numeric fields commit on completion rather than recalibrating on partial keystrokes.
- Resize at the bottom/right edge was rejected because the old origin plus new size exceeded scene bounds. Both local and server GM updates now move the resized figure just inside the boundary. Player resize rights remain denied; invalid initial placement remains rejected.

## Validation

- Server suite: 60 tests Passed (58 existing + resize boundary and player permission regressions).
- The two new server tests were run against the immutable 0.4.0 server in an isolated temp directory: boundary resize failed with the original error; player permission test passed. Both pass against the fix.
- Tree-sitter parsed all 16 C#/Java source files without syntax errors. This does not prove Unity API compatibility.
- InteractionChecks is a mandatory pre-export suite. It uses real PointerEvent instances with fixed viewport geometry and invokes the board handlers synchronously through reflection. It covers command outcomes and camera/token transforms, not OS input dispatch, Android lifecycle, visual rasterisation or hardware performance. Image checks use actual Unity PNG/JPEG decoding.
- Current build request: #11, source 17193ac35bffdd1914394fd4fe547ad84f2135d2. Compilation, test results and APK verification pending.

## Device acceptance (Not run)

Install the update over 0.4.0, preserving data. Exercise PNG/JPEG at 500 px, 2048 px, >4096 px, transparent PNG, oversized source, cancel, corrupt file, consecutive import/cancel/import and rotation while picking. Confirm memory does not accumulate. Test camera pan over tokens, empty-space pan, pinch, release one finger and continue, drag near screen edges, resize at all scene edges, locked tokens, map preview pan/resize and grid save/reopen. Check save/quit/resume/airplane mode. For online play update the server and test two clients, ownership and rejected moves. Record Android frame times before/after under the same populated scene and gestures. No claim of complete stability until these target checks pass.
