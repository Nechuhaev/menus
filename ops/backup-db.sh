#!/bin/bash
# Щотижневий бекап БД menus в Amazon S3: menus-latest (перезаписується) і датована копія backups/weekly/ -
# історія за кілька тижнів (ресторани, видалені до 15.10.2026, не знайшлись у жодному бекапі саме через перезапис).
# Локально лишаються датовані копії за KEEP_DAYS днів; у S3 старі weekly/ прибирає правило життєвого циклу бакета.
#
# Секрети - лише на сервері, не в репозиторії:
#   /root/menus.env   - рядок підключення сайту (звідси береться пароль root)
#   /root/backup.env  - AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_DEFAULT_REGION, S3_BUCKET
#
# Cron (неділя 04:00, поза годинами content-bot):
#   0 4 * * 0 /root/menus/ops/backup-db.sh >> /root/backups/backup.log 2>&1

set -euo pipefail

DB_CONTAINER=menus-mariadb-1
DB_NAME=menus
DIR=/root/backups
MIN_SIZE=100000   # дамп менший за ~100 КБ вважаємо зламаним і не вивантажуємо
KEEP_DAYS=56      # 8 тижнів датованих копій локально

log() { echo "$(date '+%F %T') $*"; }
trap 'log "ПОМИЛКА в рядку $LINENO - бекап не завершено"' ERR

set -a; source /root/backup.env; set +a
: "${S3_BUCKET:?S3_BUCKET не задано в /root/backup.env}"
DB_PASSWORD=$(grep -oP 'pwd=\K[^;]+' /root/menus.env)

mkdir -p "$DIR"
TMP="$DIR/menus-new.sql.gz"

log "Старт дампу $DB_NAME"
docker exec "$DB_CONTAINER" sh -c \
  'exec $(command -v mariadb-dump || command -v mysqldump) --single-transaction --routines --triggers --events -uroot -p"$0" "$1"' \
  "$DB_PASSWORD" "$DB_NAME" | gzip > "$TMP"

# Перевірки, щоб не перезаписати хороший бекап зламаним
gzip -t "$TMP"
SIZE=$(stat -c%s "$TMP")
[ "$SIZE" -gt "$MIN_SIZE" ] || { log "Дамп підозріло малий ($SIZE байт)"; exit 1; }
zcat "$TMP" | tail -n 1 | grep -q "Dump completed" || { log "Дамп обірвано (немає 'Dump completed')"; exit 1; }

# Локально: поточний + попередній
[ -f "$DIR/menus-latest.sql.gz" ] && mv "$DIR/menus-latest.sql.gz" "$DIR/menus-prev.sql.gz"
mv "$TMP" "$DIR/menus-latest.sql.gz"
DATED="menus-$(date +%F).sql.gz"
cp "$DIR/menus-latest.sql.gz" "$DIR/$DATED"
find "$DIR" -maxdepth 1 -name 'menus-20??-??-??.sql.gz' -mtime +"$KEEP_DAYS" -delete

docker run --rm \
  -e AWS_ACCESS_KEY_ID -e AWS_SECRET_ACCESS_KEY -e AWS_DEFAULT_REGION \
  -v "$DIR:/data:ro" --entrypoint sh amazon/aws-cli \
  -c "aws s3 cp /data/menus-latest.sql.gz 's3://$S3_BUCKET/backups/menus-latest.sql.gz' --only-show-errors &&
         aws s3 cp '/data/$DATED' 's3://$S3_BUCKET/backups/weekly/$DATED' --only-show-errors"

log "OK: $((SIZE / 1024)) КБ -> s3://$S3_BUCKET/backups/menus-latest.sql.gz і backups/weekly/$DATED"
