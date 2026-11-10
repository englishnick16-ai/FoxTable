# Размещение сервера

Сервер не размещён автоматически. Потребуется Linux/VPS или другой хост с Python 3.10+ и HTTPS. Пример ниже — инструкция для владельца хоста; команды в пользовательской инфраструктуре не выполнялись.

## Небольшая закрытая группа

1. Скопировать папку `Server` на свой хост в `/opt/foxtable`.
2. Создать системного пользователя `foxtable` и каталог данных `/var/lib/foxtable`, доступный ему на запись.
3. Использовать systemd-сервис, который слушает только localhost:

```ini
[Unit]
Description=FoxTable encounter server
After=network.target

[Service]
User=foxtable
WorkingDirectory=/opt/foxtable
ExecStart=/usr/bin/python3 /opt/foxtable/server.py --host 127.0.0.1 --port 8787 --database /var/lib/foxtable/foxtable.sqlite3
Restart=on-failure
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ReadWritePaths=/var/lib/foxtable

[Install]
WantedBy=multi-user.target
```

4. Настроить HTTPS на существующем reverse proxy. Минимальный фрагмент Nginx внутри уже настроенного TLS server-блока:

```nginx
client_max_body_size 9m;
location / {
    proxy_pass http://127.0.0.1:8787;
    proxy_set_header Host $host;
    proxy_set_header Authorization $http_authorization;
    proxy_read_timeout 30s;
}
```

5. Проверить `https://ваш-домен/health`. Ответ должен содержать `"status":"ok"`.
6. В приложениях мастера и игроков указать один и тот же HTTPS-адрес.

Стандартный Python HTTP-сервер здесь выполняет API небольшой группы. Это не готовая масштабируемая платформа: перед открытой регистрацией нужны ограничение частоты запросов/размера базы, управление комнатами и нагрузочные проверки. Не запускать несколько независимых копий этого процесса на одной SQLite-базе без отдельной проверки транзакций: синхронизация команд рассчитана на один процесс.

## Резервная копия

Использовать SQLite backup API, который корректно учитывает WAL. Не копировать только основной файл работающей базы простым `cp`.

```python
import sqlite3
with sqlite3.connect('/var/lib/foxtable/foxtable.sqlite3') as source:
    with sqlite3.connect('/var/lib/foxtable/backup.sqlite3') as target:
        source.backup(target)
```

В базе хранятся кампании, изображения, заметки и хеши ключей. Для восстановления роли мастер также должен сохранить свой личный ключ из приложения. Сам ключ сервер в открытом виде не хранит. После переноса базы на новый домен восстановить сессию через экран входа, используя код комнаты и личный ключ.
