#!/bin/bash
# Деплой сайту (menus-web) без простою: новий контейнер стартує поруч зі старим на вільному порту (5001/5002),
# старий зупиняється лише тоді, коли новий відповідає. Не піднявся - старий працює далі, деплой падає.
#
# Вмикається файлом /root/menus/ops/zero-downtime.on - ПІСЛЯ того, як nginx знає обидва порти
# (upstream 127.0.0.1:5001 і 127.0.0.1:5002, див. ops/README-zero-downtime.md). Без файла - як раніше:
# один контейнер menus-web на 5001 з хвилиною простою.
set -e
cd /root/menus
RUN_ARGS=(--restart unless-stopped --network menus_default --network-alias menus-web --env-file /root/menus.env
          -e ASPNETCORE_ENVIRONMENT=Production -e ASPNETCORE_URLS=http://+:5001 -v /root/menus/dataprotection-keys:/app/Keys)

running() { docker ps --format '{{.Names}}' | grep -qx "$1"; }

if [ ! -f /root/menus/ops/zero-downtime.on ]; then
  docker rm -f menus-web menus-web-a menus-web-b >/dev/null 2>&1 || true
  docker run -d --name menus-web -p 5001:5001 "${RUN_ARGS[@]}" menus-web:latest
  exit 0
fi

# який контейнер зараз обслуговує сайт: старий menus-web (5001) / menus-web-a (5001) / menus-web-b (5002)
if running menus-web-b; then OLD=menus-web-b; NEW=menus-web-a; PORT=5001
elif running menus-web-a; then OLD=menus-web-a; NEW=menus-web-b; PORT=5002
elif running menus-web; then OLD=menus-web; NEW=menus-web-b; PORT=5002
else OLD=""; NEW=menus-web-a; PORT=5001
fi

docker rm -f "$NEW" >/dev/null 2>&1 || true
docker run -d --name "$NEW" -p "$PORT:5001" "${RUN_ARGS[@]}" menus-web:latest

# чекаємо, поки новий відповідає (до 3 хв)
ok=""
for i in $(seq 1 90); do
  code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "http://127.0.0.1:$PORT/uk" || true)
  case "$code" in 200|301|302) ok=1; break ;; esac
  sleep 2
done
if [ -z "$ok" ]; then
  echo "Новий контейнер $NEW не відповів на :$PORT - лишаю старий ($OLD), деплой зупинено"
  docker logs --tail 60 "$NEW" || true
  docker rm -f "$NEW" || true
  exit 1
fi

echo "Новий $NEW відповідає на :$PORT"
sleep 5   # nginx уже шле запити на обидва порти; запити, що йдуть зараз у старий, встигають завершитись
if [ -n "$OLD" ]; then docker stop -t 20 "$OLD" >/dev/null && docker rm "$OLD" >/dev/null; fi
echo "Старий ${OLD:-немає} зупинено; сайт працює з $NEW (:$PORT)"
