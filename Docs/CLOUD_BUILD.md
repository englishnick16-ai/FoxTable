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
- Идентификатор приложения: `com.foxtable.mobile`, версия `0.2.0`.
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
