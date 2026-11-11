# Облачная сборка FoxTable

Облачный проект создан 11 ноября 2026. Unity Organization: `9072467250824`.
Unity Project ID: `da845b0f-e87a-471f-9708-c19d9fbae20d`.
Репозиторий: `https://github.com/englishnick16-ai/FoxTable` (public).

## Настройки Unity Build Automation

- Исходники: ветка `main`, папка проекта — корень репозитория.
- Платформа: Android, APK, ARM64 / IL2CPP.
- Unity: базовая версия проекта `6000.0.62f1`; для первой облачной сборки выбрана `6000.0.85f2`.
- Сборочная машина: Windows 11 24H2, Windows Micro (8 vCPU, 16 GB RAM), Free tier eligible; Android SDK 35.
- Pre-Export Method: `FoxTable.Editor.FoxTableBuild.Prepare`.
- Scenes: `Assets/FoxTable/Scenes/Table.unity` (метод Prepare создаёт сцену через Unity API перед экспортом).
- Идентификатор приложения: `com.foxtable.mobile`, версия `0.4.0` (задаётся Prepare).
- Для первой проверки использовать автоматически создаваемую отладочную подпись. Публикационный keystore в репозитории не хранить.
- Автоматический запуск на каждый коммит выключен: запускать вручную, пока не подтверждена сборка и расход бесплатных минут.

Конфигурация `foxtable-android-apk` сохранена. Репозиторий подключён через GitHub; токен в исходниках не хранится. Первая чистая сборка №1 запущена 11 ноября 2026 года. Статус запуска подтверждён в Unity Dashboard; успешная компиляция и APK пока не подтверждены.

Сборка №1: https://cloud.unity.com/organizations/9072467250824/projects/da845b0f-e87a-471f-9708-c19d9fbae20d/cloud-build/buildtargets/foxtable-android-apk/builds/1

Прохождение серверных тестов и синтаксического анализа не подтверждает сборку Android. Итог определять по журналу Build Automation и наличию APK.

## Исправление после сборки №1

Unity 6000.0.85f2 остановил импорт: `com.unity.modules.textcorefontengine`, `com.unity.modules.textcoretextengine` и `com.unity.modules.textrendering` не существуют как встроенные пакеты. Эти записи удалены. Добавлены встроенные `com.unity.modules.androidjni` и `com.unity.modules.jsonserialize`, используемые Android-плагином и протоколом клиента. UIElements остаётся включённым.

Повторная проверка — сборка APK с исправленным manifest в той же конфигурации; регион Americas оставлен без изменения. Сборка №1 использовала исходный commit `1145d3a8621c6a9f3b893283704a191637f3b2de` и не создала APK.

## Исправление после сборки №2

Сборка №2 (`e4df99e80ae86b9ed459f034730571f2254e9794`) разрешила зависимости и дошла до вызова `Prepare`. Экспорт остановился с `Cannot create a new scene additively with an untitled scene unsaved`. Подготовка теперь создаёт стартовую сцену в режиме `Single`, не зависящем от несохранённой начальной сцены облачного Editor. При интерактивном запуске сначала предлагается сохранить изменённые сцены; сохранённая конфигурация открытых сцен восстанавливается после создания Table. В batch mode остаётся открытой созданная Table. Результат проверяется следующей Android-сборкой; APK сборкой №2 не создан.

## Проверка APK сборки №3

Сборка №3 (`c83171954b75496af99220f0ae1bb4fb0697880b`) успешно завершила Unity, IL2CPP и Gradle и создала APK (19 955 728 байт). Проверены ZIP CRC, package `com.foxtable.mobile`, versionName `0.2.0`, versionCode `3`, minSdk `26`, targetSdk `37`, ARM64, наличие `libunity.so`/`libil2cpp.so` и подписи APK v2. Это проверка упаковки, не запуск на устройстве.

Проверка DEX выявила отсутствие класса `com.foxtable.FoxGallery` при наличии activity в манифесте. Java лежал в устаревшем каталоге `src/com`, который не был включён в source set Gradle. Плагин переведён на `src/main/java` и `src/main/AndroidManifest.xml`; добавлен собственный `build.gradle` с параметрами Unity и consumer keep rule для вызова через JNI. APK №3 не выдаётся как итоговый: требуется повторная сборка и проверка наличия класса в DEX.

## Итоговая сборка №4

Исходный commit `3bbb932b92aa485ec9983df5a27f65c1cd5cd152`. Unity, IL2CPP и Gradle завершились успешно; APK скачан. Файл: 19 956 960 байт, SHA-256 `5bbf795572f6d4dffef972347fa317426ae64aeddc9d11892b36cd2b61b10410`. Package `com.foxtable.mobile`, versionName `0.2.0`, versionCode `4`, minSdk `26`, фактический targetSdk `37`, ARM64, подпись APK v2. Поле SDK облачной конфигурации было установлено в 35, но автоматический выбор target API в Unity дал 37; при воспроизведении ориентироваться на AndroidManifest итогового пакета.

`Tools/verify_apk.py` прошёл: целый ZIP, библиотеки Unity/IL2CPP, фактический класс FoxGallery в DEX, callback OnAndroidImage в metadata. Androguard подтвердил activity, методы `open`/`onActivityResult`, версии, разрешение интернета и наличие подписи. Проверка на APK №3 до исправления воспроизводит отсутствие класса. Установка, UI и игровой runtime ещё не проверены; онлайн-сервер не развёрнут.

Артефакты: https://cloud.unity.com/organizations/9072467250824/projects/da845b0f-e87a-471f-9708-c19d9fbae20d/cloud-build/buildtargets/foxtable-android-apk/builds/4/artifacts

## Сборка 0.3.0 с локальной игрой

№6: commit `4f11e6167ce3888a2eee3746ab2ee47e6054f9f8`, та же Android-конфигурация, pre-export `FoxTable.Editor.FoxTableBuild.Prepare`. В Prepare запускаются LocalGameChecks: 33 проверки прошли, затем build successful. Файл скачан и назван `FoxTable-0.3.0.apk`; versionName проверен в манифесте, versionCode=6. В настройках Cloud Build имя выходного файла ещё может содержать 0.2.0 — версия приложения задаётся Prepare и равна 0.3.0. №5 отменена до компиляции после исправления System.Random в проверках.

Хеш APK и ограничения — в Docs/VALIDATION.md. Регион, тариф, доступы и настройки подписи не менялись; сертификат 0.3.0 совпадает с 0.2.0. Установка на Android пока не проверена.

## Итоговая сборка 0.4.0 №10

Сборка №10 завершилась успешно: runtime commit `89eb526906ed74106fee6640760529051179e463`, Unity 6000.0.85f2, Android ARM64/IL2CPP. APK `FoxTable-0.4.0.apk` скачан и проверен: 20 239 320 байт, SHA-256 `b8da5dc50e8fea0d68f6af50c35f549748cf1ecefd29a0fd03acb6186ab72d0b`, package `com.foxtable.mobile`, versionName `0.4.0`, versionCode `10`, minSdk26/targetSdk37. Подпись v2 присутствует, DER-сертификат совпадает с 0.3.0; полная криптографическая проверка подписи отдельно не проводилась.

58 серверных тестов прошли. Обязательный Prepare выполнил 33 LocalGameChecks и 53 WorkspaceChecks (86 проверок Unity); в журнале №10: `[2026-11-11T17:30:13Z - Unity] FoxTable workspace checks passed: 53`. Это batch-проверки логики, геометрии, ресурсов и VisualElement, не запуск на Android.

№8 остановилась на проверке округления автоподгонки; расчёт исправлен без отключения проверки. №9 прошла 33+50 проверок и отменена для включения явного 2D-импорта стартовой карты. В №10 это исправление и три новых startup checks включены и прошли. Имя артефакта в конфигурации может содержать старую версию; versionName итогового манифеста 0.4.0.

Проверены все 32 Texture2D миниатюры с прозрачностью и Sanctuary как Texture2D 1500×1100 в APK. Установка и Android runtime не проверены. Тариф, регион, доступы и ключ подписи не менялись.


## Исправления 0.4.1 — сборка ожидается

Runtime/проверки: f953a1d291291ff9bbba66f213bf33c692df3da8 (production-код f5c10002b070d465c57c3acf6b8dc6a942c0d9ae). Исходный дефект и изменения описаны в Docs/BUGS-0.4.1.md, управление — Docs/WORKSPACE.md. 60 серверных тестов прошли, все 16 C#/Java файлов разобраны Tree-sitter без синтаксических ошибок. Увеличение фигуры у границы воспроизведено на исходном сервере 0.4.0 и проходит на исправленном.

№11 отменена после 11:36 в Sent to Builder без начала компиляции/получения commit ID. Один повторный запрос — №12, конфигурация прежняя; результат ожидается. InteractionChecks добавлены в обязательный Prepare: синтетические PointerEvent-сценарии (включая отпускание без позиции и отмену), камера/туман, реальные PNG/JPEG и освобождение CPU-копий. Их выполнение в Unity пока не подтверждено. Не переносить результаты 0.4.0 на новый код. Новый APK пока не получен, Android lifecycle/плавность/исчезновение пользовательского вылета не проверены. Сохранения не удалять; не менять тариф, регион и доступы ради ускорения сборки.
