# EtsAreaTool

ETS Area Whitelist Encryption Tool — 供**授權主管**於工作機使用的加密小工具。

對應 ETS 規格書 §5.3 ~ §5.5。

> ⚠ 本工具**僅限授權主管工作機使用**，絕不可部署至正式 ETS 伺服器。

---

## 指令總覽

| 指令 | 用途 |
|---|---|
| `generate-key` | 產生新的 256-bit AES 金鑰（Base64）|
| `encrypt`      | 將明文 `whitelist.json` 加密為 `area_whitelist.enc` |
| `inspect`      | 解密檢視 `.enc` 檔內容（稽核用）|

---

## 編譯與發佈

```bash
# 開發測試
dotnet run -- generate-key

# 發佈為 Windows 單一執行檔（於 Solution 根目錄執行）
dotnet publish tools/EtsAreaTool/EtsAreaTool.csproj \
    -c Release -r win-x64 --self-contained false \
    -o tools/EtsAreaTool/dist/
# 產出：tools/EtsAreaTool/dist/ets-area-tool.exe
```

---

## 使用範例

### 1. 首次部署

```bash
# Step 1：產生金鑰
ets-area-tool generate-key

# Step 2：建立白名單 JSON
cat > whitelist.json << 'EOF'
{
  "event_areaList": ["林口院區", "台中院區"],
  "generated_by": "IT-Manager-Joseph"
}
EOF

# Step 3：加密
ets-area-tool encrypt -i whitelist.json -o area_whitelist.enc -k "<金鑰>"

# Step 4：部署
#   - 將 area_whitelist.enc 複製到 ETS 伺服器
#   - 設定環境變數：ETS_AREA_WHITELIST_KEY=<金鑰>
#   - 重啟 ETS 服務

# Step 5：銷毀本機明文（Linux）
shred -u whitelist.json
# Windows：
# cipher /w:. （覆寫當前目錄空間）
# del whitelist.json
```

### 2. 不限制模式（空陣列）

```json
{
  "event_areaList": [],
  "generated_by": "IT-Manager-Joseph"
}
```

### 3. 稽核當前白名單

```bash
ets-area-tool inspect -i area_whitelist.enc -k "<金鑰>"
```

---

## 白名單 JSON 格式

| 欄位 | 必填 | 說明 |
|---|---|---|
| `event_areaList` | ✅ | 院區名稱陣列。空陣列 `[]` = 不限制模式 |
| `generated_by`   | ⬜ | 授權主管識別字串（未填則自動使用 OS 帳號）|
| `generated_at`   | ⬜ | 自動補上 ISO 8601 時間 |
| `version`        | ⬜ | 版本號（預設 1）|

---

## 安全注意事項

- ❌ 勿將金鑰寫入 git repository
- ❌ 勿透過電子郵件或 IM 傳送金鑰（改用密碼管理工具）
- ❌ 勿在共用電腦使用（避免金鑰被 shell history 記錄）
- ✅ 加密完成後立即刪除明文 `whitelist.json`

---

## 加密規格

- **演算法**：AES-256-GCM（對稱加密 + 內建完整性驗證）
- **金鑰長度**：256 bit（32 byte）
- **檔案格式**：`[Nonce(12) | Tag(16) | Ciphertext(N)]`

## 退出碼

| Code | 意義 |
|---|---|
| 0 | 成功 |
| 1 | 參數錯誤 |
| 2 | 執行錯誤 |
