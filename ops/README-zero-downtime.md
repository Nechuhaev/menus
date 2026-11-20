# Деплой сайту без простою

`ops/deploy-web.sh` (викликає `deploy.yml`) запускає новий контейнер сайту поруч зі старим на вільному порту
(5001 або 5002) і зупиняє старий лише тоді, коли новий відповідає. Якщо новий не піднявся - старий працює далі.

Щоб це працювало, nginx має знати обидва порти. Один раз на сервері:

1. Знайти конфіг сайту: `grep -rln "127.0.0.1:5001" /www/server/panel/vhost/nginx/`
2. На початок цього файла (перед `server {`) додати:

       upstream menus_web {
           server 127.0.0.1:5001 max_fails=1 fail_timeout=10s;
           server 127.0.0.1:5002 max_fails=1 fail_timeout=10s;
       }

3. У тому ж файлі замінити `proxy_pass http://127.0.0.1:5001;` на `proxy_pass http://menus_web;`
   і поруч додати рядок `proxy_next_upstream error timeout http_502 http_503;`
4. Перевірити й перезавантажити: `nginx -t && nginx -s reload`
5. Увімкнути новий деплой: `touch /root/menus/ops/zero-downtime.on`

Без файла `zero-downtime.on` деплой працює як раніше (один контейнер `menus-web`, ~1 хв простою).
Логи сайту після переходу: `docker logs --tail 60 $(docker ps --format '{{.Names}}' | grep '^menus-web')`.
