# ETS M3 負載測試規劃（WBS 1.3.18）

> 對應規格書 §16.1 非功能需求：P95 ≤ 500ms，500 人並發，錯誤率 < 0.1%

---

## 測試工具

- **k6**（JavaScript 腳本，CI/CD 友善）
- 執行環境：Windows Server 測試機（與 IIS 同網段）

---

## 情境 1：HIS 事件觸發壓力測試

**目標**：模擬 HIS 連續觸發 100 個事件

```javascript
// k6/his-trigger.js
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '30s', target: 10 },   // 暖機
    { duration: '60s', target: 50 },   // 壓測
    { duration: '30s', target: 0  },   // 降溫
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'],   // P95 ≤ 500ms
    http_req_failed:   ['rate<0.001'],  // 錯誤率 < 0.1%
  },
};

let counter = 0;

export default function () {
  counter++;
  const eventId = `E${Date.now()}X${String(counter).padStart(3,'0')}`;
  const payload = JSON.stringify({
    event_ID:           eventId,
    event_type:         'a',
    event_time:         '2024-01-01 12:00:00',
    event_area:         '林口院區',
    event_summary:      '壓測事件',
    event_source:       'HIS',
    event_flex_msg_items: '["15 分鐘內","30 分鐘內","無法返回院區"]',
    event_commander:    '["joseph"]',
    event_groups:       [{ chatGP: '(A001)消防組' }],
    event_responders:   [{ acct: 'joseph', role: 'commander', chatGP: '(A001)消防組' }],
  });

  const res = http.post(
    'https://ets.hospital.internal/api/v1/his/event-trigger',
    payload,
    { headers: { 'Content-Type': 'application/json', 'X-ETS-API-Key': __ENV.ETS_API_KEY } }
  );

  check(res, {
    'status 200': (r) => r.status === 200,
    'success true': (r) => JSON.parse(r.body).success === true,
  });

  sleep(0.5);
}
```

---

## 情境 2：Webhook 並發壓力測試

**目標**：模擬 500 人同時回覆 Flex Message

```javascript
// k6/webhook-concurrent.js
import http from 'k6/http';
import { check } from 'k6';
import { SharedArray } from 'k6/data';
import crypto from 'k6/crypto';

export const options = {
  vus: 500,
  duration: '60s',
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed:   ['rate<0.001'],
  },
};

const accounts = new SharedArray('accounts', function () {
  // 預先建立 500 個測試帳號
  return Array.from({ length: 500 }, (_, i) => `testuser${i + 1}`);
});

export default function () {
  const account   = accounts[__VU - 1];
  const timestamp = Date.now();
  const data      = `id=E_LOAD_TEST_EVENT_001&feedback=${encodeURIComponent('15 分鐘內')}`;

  const bodyObj = {
    destination: 'test-channel-a',
    events: [{
      type:      'postback',
      timestamp,
      source:    { type: 'user', userId: account },
      postback:  { data }
    }]
  };
  const bodyStr = JSON.stringify(bodyObj);

  // 計算 HMAC-SHA256 簽章
  const sig = crypto.hmac('sha256', __ENV.CHANNEL_SECRET, bodyStr, 'base64');

  const res = http.post(
    'https://ets.hospital.internal/api/v1/webhooks/teamplus/postback',
    bodyStr,
    {
      headers: {
        'Content-Type':         'application/json',
        'X-TeamPlus-Signature': sig,
      }
    }
  );

  check(res, {
    'status 200': (r) => r.status === 200,
  });
}
```

---

## 情境 3：Dashboard 輪詢基線測試

**目標**：模擬 30 個 Dashboard 同時開啟，持續輪詢

```javascript
// k6/dashboard-polling.js
export const options = {
  vus: 30,
  duration: '120s',
  thresholds: {
    http_req_duration: ['p(95)<200'],  // Dashboard 要求更快
  },
};

export default function () {
  http.get('https://ets.hospital.internal/api/v1/dashboard/events/E001/summary',
    { headers: { Cookie: `jwt=${__ENV.JWT_TOKEN}` } });
  sleep(5);  // 每 5 秒輪詢一次
}
```

---

## 執行指令

```powershell
# 情境 1
k6 run -e ETS_API_KEY=test-channel-secret-a k6/his-trigger.js

# 情境 2
k6 run -e CHANNEL_SECRET=test-channel-secret-a k6/webhook-concurrent.js

# 情境 3
k6 run -e JWT_TOKEN=<jwt> k6/dashboard-polling.js
```

---

## 通過標準（§16.1）

| 指標 | 目標 | 測試工具 |
|---|---|---|
| P95 回應時間 | ≤ 500ms | k6 `http_req_duration` |
| 錯誤率 | < 0.1% | k6 `http_req_failed` |
| 500 人並發 Webhook | 無逾時 | 情境 2 |
| DB 連線池飽和 | 不觸發 | SQL Server DMV 監控 |
