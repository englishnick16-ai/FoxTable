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
