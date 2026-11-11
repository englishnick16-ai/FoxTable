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

## Итоговая сборка №4

Исходный commit `3bbb932b92aa485ec9983df5a27f65c1cd5cd152`. Unity, IL2CPP и Gradle завершились успешно; APK скачан. Файл: 19 956 960 байт, SHA-256 `5bbf795572f6d4dffef972347fa317426ae64aeddc9d11892b36cd2b61b10410`. Package `com.foxtable.mobile`, versionName `0.2.0`, versionCode `4`, minSdk `26`, фактический targetSdk `37`, ARM64, подпись APK v2. Поле SDK облачной конфигурации было установлено в 35, но автоматический выбор target API в Unity дал 37; при воспроизведении ориентироваться на AndroidManifest итогового пакета.

`Tools/verify_apk.py` прошёл: целый ZIP, библиотеки Unity/IL2CPP, фактический класс FoxGallery в DEX, callback OnAndroidImage в metadata. Androguard подтвердил activity, методы `open`/`onActivityResult`, версии, разрешение интернета и наличие подписи. Проверка на APK №3 до исправления воспроизводит отсутствие класса. Установка, UI и игровой runtime ещё не проверены; онлайн-сервер не развёрнут.

Артефакты: https://cloud.unity.com/organizations/9072467250824/projects/da845b0f-e87a-471f-9708-c19d9fbae20d/cloud-build/buildtargets/foxtable-android-apk/builds/4/artifacts

## Сборка 0.3.0 с локальной игрой

№6: commit `4f11e6167ce3888a2eee3746ab2ee47e6054f9f8`, та же Android-конфигурация, pre-export `FoxTable.Editor.FoxTableBuild.Prepare`. В Prepare запускаются LocalGameChecks: 33 проверки прошли, затем build successful. Файл скачан и назван `FoxTable-0.3.0.apk`; versionName проверен в манифесте, versionCode=6. В настройках Cloud Build имя выходного файла ещё может содержать 0.2.0 — версия приложения задаётся Prepare и равна 0.3.0. №5 отменена до компиляции после исправления System.Random в проверках.

Хеш APK и ограничения — в Docs/VALIDATION.md. Регион, тариф, доступы и настройки подписи не менялись; сертификат 0.3.0 совпадает с 0.2.0. Установка на Android пока не проверена.

## Запрос сборки исходников 0.4

Сохранён commit `89eb526906ed74106fee6640760529051179e463`. Конфигурация прежняя: Android ARM64/IL2CPP, Unity 6000.0.85f2, pre-export FoxTable.Editor.FoxTableBuild.Prepare, с проверками LocalGameChecks и WorkspaceChecks. №8 остановлена проверкой округления автоподгонки; №9 прошла 33 + 50 проверок и отменена для включения исправления TextureImporter стартовой карты. Последний запрос для указанного commit отправлен, но номер и результат не подтверждены из-за отключившейся среды. Нельзя считать APK 0.4 собранным или доступным, пока это не проверено в Build history.

Тариф, регион, доступы и ключ подписи не менялись. После восстановления среды проверить startup checks, скачать APK и проверить реальную версию/подпись/ресурсы. Подробности — Docs/VALIDATION.md.
