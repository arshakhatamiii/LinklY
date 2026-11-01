# راهنمای جامع دستورات اجرایی و توسعه پروژه (Project Commands Cheatsheet)

این سند شامل تمامی دستورات لازم برای اجرا، تست، سنجش کارایی و توسعه سرویس کوتاه‌کننده لینک (LinklY) است.

---

## ۱. اجرای پروژه با داکر (Docker Compose)

### اجرای کامل سرویس‌ها (API + PostgreSQL)
```bash
docker compose up --build -d
```
- **آدرس سرویس:** `http://localhost:5100`
- **پورت دیتابیس:** `localhost:5433` (نام کاربری و رمز: `postgres`، نام دیتابیس: `urlshortener`)

### بررسی وضعیت کانتینرها
```bash
docker compose ps
```

### مشاهده لاگ‌های زنده سرور (Serilog)
```bash
docker compose logs -f api
```

### خاموش کردن کانتینرها (حفظ اطلاعات دیتابیس)
```bash
docker compose down
```

### خاموش کردن و ریست کامل اطلاعات دیتابیس (حذف Volume)
```bash
docker compose down -v
```

---

## ۲. دستورات تست اندپوینت‌ها با cURL

### الف) بررسی سلامت سرویس (Health Check)
```bash
curl -i http://localhost:5100/health
```

### ب) ایجاد لینک کوتاه ساده (کد خودکار Base62)
```bash
curl -i -X POST http://localhost:5100/api/links \
  -H "Content-Type: application/json" \
  -d '{"url": "https://github.com/dotnet/aspnetcore"}'
```

### ج) ایجاد لینک با نام مستعار دلخواه (Custom Alias)
```bash
curl -i -X POST http://localhost:5100/api/links \
  -H "Content-Type: application/json" \
  -d '{"url": "https://github.com", "alias": "my-github-link"}'
```

### د) ایجاد لینک با تاریخ انقضا (Expiration)
```bash
curl -i -X POST http://localhost:5100/api/links \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com", "expiresAt": "2026-12-31T23:59:59Z"}'
```

### ه) ایجاد لینک با نام مستعار و تاریخ انقضا همزمان
```bash
curl -i -X POST http://localhost:5100/api/links \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://example.com/promo",
    "alias": "black-friday",
    "expiresAt": "2026-12-31T23:59:59Z"
  }'
```

### و) تست ریدایرکت (دریافت کد ۳۰۲)
```bash
curl -i http://localhost:5100/my-github-link
```

### ز) تست نام مستعار تکراری (دریافت کد ۴۰۹ Conflict)
```bash
curl -i -X POST http://localhost:5100/api/links \
  -H "Content-Type: application/json" \
  -d '{"url": "https://google.com", "alias": "my-github-link"}'
```

### ح) تست کد ناموجود (دریافت کد ۴۰۴ Not Found)
```bash
curl -i http://localhost:5100/nonexistent-code
```

---

## ۳. دستورات تست و توسعه با .NET CLI

### کامپایل و بیلد کل راه‌حل
```bash
dotnet build
```

### اجرای تمامی تست‌ها (Unit Tests + Testcontainers Integration Tests)
```bash
dotnet test
```

### اجرای اختصاصی تست‌های واحد الگوریتم Base62
```bash
dotnet test tests/UrlShortener.UnitTests
```

### اجرای اختصاصی تست‌های یکپارچگی اندپوینت‌ها
```bash
dotnet test tests/UrlShortener.IntegrationTests
```

### اجرای مستقیم پروژه API به صورت لوکال (بدون کانتینر API)
```bash
dotnet run --project src/UrlShortener.Api
```

---

## ۴. مدیریت میگریشن‌های دیتابیس (EF Core Migrations)

### افزودن میگریشن جدید
```bash
dotnet ef migrations add <MigrationName> \
  --project src/UrlShortener.Infrastructure \
  --startup-project src/UrlShortener.Api \
  --output-dir Persistence/Migrations
```

### اعمال دستی میگریشن‌ها روی پایگاه داده
```bash
dotnet ef database update \
  --project src/UrlShortener.Infrastructure \
  --startup-project src/UrlShortener.Api
```

### حذف آخرین میگریشن اعمال‌نشده
```bash
dotnet ef migrations remove \
  --project src/UrlShortener.Infrastructure \
  --startup-project src/UrlShortener.Api
```

---

## ۵. اجرای آزمون بار و کارایی با k6 (Load Testing)

ابزار **k6** مستقیماً روی سیستم مک شما نصب است (`/opt/homebrew/bin/k6`) و همچنین می‌توانید آن را از طریق داکر اجرا کنید.

### الف) اجرای مستقیم در ترمینال مک (ساده‌ترین روش):
```bash
# اجرای سناریوی استاندارد اسکریپت (۵۰ کاربر، ۳۰ ثانیه):
k6 run loadtest/redirect.js
```

### ب) اجرای با داکر (بدون وابستگی به سیستم‌عامل):
```bash
docker run --rm -i \
  -v $(pwd)/loadtest:/loadtest \
  -e BASE_URL=http://host.docker.internal:5100 \
  grafana/k6 run /loadtest/redirect.js
```

### ج) دستورات و فلگ‌های کاربردی k6 برای سناریوهای مختلف:

#### ۱. تست سریع عملکردی (Smoke Test - یک کاربر برای ۵ ثانیه):
```bash
k6 run --vus 1 --duration 5s loadtest/redirect.js
```

#### ۲. تغییر تعداد کاربران همزمان (مثلاً ۱۰۰ کاربر):
```bash
k6 run --vus 100 --duration 20s loadtest/redirect.js
```

#### ۳. تست استرس سنگین (مثلاً ۲۰۰ کاربر برای ۱ دقیقه):
```bash
k6 run --vus 200 --duration 1m loadtest/redirect.js
```

#### ۴. تغییر آدرس سرور هدف (مثلاً سرور استیجینگ یا پورت دیگر):
```bash
k6 run -e BASE_URL=http://localhost:5100 loadtest/redirect.js
```

#### ۵. ذخیره گزارش نهایی در یک فایل JSON:
```bash
k6 run --summary-export=loadtest/results.json loadtest/redirect.js
```

#### ۶. اجرای بدون لاگ‌های اضافی (حالت بی‌صدا):
```bash
k6 run -q loadtest/redirect.js
```

---

## ۶. دستورات گیت (Git Commands)

### مشاهده وضعیت فایل‌ها
```bash
git status
```

### مشاهده تاریخچه کامیت‌ها به صورت فشرده
```bash
git log --oneline -n 10
```

### استیج، کامیت و پوش کردن تغییرات به گیت‌هاب
```bash
git add .
git commit -m "feat(scope): your commit message"
git push
```
