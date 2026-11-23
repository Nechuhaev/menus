# TipMyMenu у каталозі Poster

## Назва
TipMyMenu - меню закладу в Google, QR-меню і меню на сайт

## Короткий опис (UA)
Меню з каси Poster - на сторінці закладу в TipMyMenu, у QR-меню на столах і на вашому сайті. Страви, ціни й фото оновлюються з Poster самі.

## Опис (UA)
TipMyMenu - каталог меню закладів України. Підключіть Poster, і меню з каси з'явиться:

- на сторінці закладу в TipMyMenu - її знаходять у Google за назвою закладу й стравами;
- у QR-меню на столах - гість сканує код і бачить страви з фото й цінами;
- на вашому сайті - готовий код для вставки, вигляд налаштовується.

Що змінили в Poster - те й на сайті: нові страви, ціни, фото, приховані позиції. Стоп-лист можна вести й з Telegram-бота.

Власник щомісяця отримує звіт: скільки разів заклад побачили в Google, скільки переходів, сканів QR і оцінок страв.

Безкоштовно. Застосунок лише читає меню й точки продажу - замовлення, гроші й клієнтів не бачить і нічого в Poster не змінює.

## Short description (EN)
Your Poster menu on TipMyMenu: a venue page found in Google, a QR menu for tables and a menu for your website. Dishes, prices and photos update from Poster automatically.

## Description (EN)
TipMyMenu is a catalog of restaurant menus in Ukraine. Connect Poster and your menu appears:

- on your venue page on TipMyMenu, which people find in Google by venue name and dishes;
- in a QR menu on tables, where guests see dishes with photos and prices;
- on your own website, with an embed code and adjustable design.

Whatever you change in Poster shows up on the site: new dishes, prices, photos, hidden items. The stop list can also be managed from the Telegram bot.

Every month the owner gets a report: Google impressions, clicks, QR scans and dish ratings.

Free. The app only reads the menu and spots. It does not see orders, payments or customers and changes nothing in Poster.

## Інструкція підключення (UA)
1. Натисніть «Встановити» на сторінці застосунку в Poster і дозвольте доступ до меню.
2. Якщо у вас кілька закладів - оберіть точку продажу.
3. Вкажіть назву закладу, місто й телефон або Telegram.
4. Протягом робочого дня ми створимо сторінку закладу з меню з каси й напишемо вам.
5. Далі меню оновлюється з Poster саме. QR-код на стіл, наліпку на вітрину й код для сайту беріть у кабінеті TipMyMenu: https://tipmymenu.com/uk/restaurants/about

Вже є сторінка закладу на TipMyMenu? Увійдіть у кабінет і натисніть «Підключити Poster» у блоці «Каса Poster» - підключення одразу.

## Нотатка для модерації («Нотатка щодо оновлення»)
Відео: <посилання на відео>

TipMyMenu читає лише menu.getCategories, menu.getProducts і spots.getSpots; вебхуки меню ставлять заклад у чергу оновлення, вебхук application/removed відключає заклад.
Сценарій для перевірки з нового акаунта: «Встановити» -> форма (назва, місто, контакт) -> заявка; після створення сторінки закладу меню з каси з'являється на ній і в QR-меню.
Для власника з наявною сторінкою: кабінет https://tipmymenu.com/uk/restaurants/about -> «Каса Poster» -> «Підключити Poster».
Тестові доступи до кабінету TipMyMenu надамо на запит.

## Сценарій відео (1-2 хв) і GIF
1. Poster -> Застосунки -> TipMyMenu -> «Встановити» -> дозвіл доступу.
2. Форма TipMyMenu: точка, назва, місто, контакт -> «Підключити» -> «Дякуємо».
3. Адмінка TipMyMenu -> «Запити» -> запит «Poster: акаунт» -> «Прив'язати Poster».
4. Після синхронізації: сторінка закладу з меню з каси; телефон сканує QR - те саме меню.
5. Зміна ціни в Poster -> після синхронізації нова ціна на сторінці.
GIF для інструкції: кроки 1-2 (до 15 с).
