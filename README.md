# Color Alert

Color Alert 是 Windows 10/11 的輕量桌面工具。使用者框選一個螢幕區域後，程式會定期判斷該區域是否仍為黑色；當畫面穩定轉為非黑色時播放一次 Windows 系統提示音，回復黑色後才重新待命。

## 功能

- 支援多螢幕、負座標與不同 DPI 的矩形選區
- 可調整 RGB 黑色容差與非黑像素觸發比例
- 連續三次取樣確認，並以一半觸發比例作為復歸門檻
- 最小化至系統匣，可從選單開始、暫停、重選或退出
- 設定保存在 `%LocalAppData%\ColorAlert\settings.json`
- 不連網、不要求管理員權限、不保存螢幕影像

## 系統需求與限制

- Windows 10 或 Windows 11，x64
- 原始碼建置需要 .NET 10 SDK；發布版本為 self-contained，不需另裝 Runtime
- 使用 GDI 擷取一般桌面程式與瀏覽器；不保證支援 DRM 影片、UAC 安全桌面、硬體 overlay 或獨占全螢幕遊戲

## 建置與測試

```powershell
dotnet build ColorAlert.slnx -c Release
dotnet test tests/ColorAlert.Core.Tests/ColorAlert.Core.Tests.csproj -c Release
```

核心偵測測試可在任何支援 .NET 10 的平台執行。WPF、螢幕擷取、系統提示音與多螢幕互動仍須在 Windows 實機驗證。

## 發布單一執行檔

```powershell
dotnet publish src/ColorAlert/ColorAlert.csproj -p:PublishProfile=win-x64
```

輸出位於 `artifacts/win-x64/ColorAlert.exe`。這是免安裝的 Windows x64 單一執行檔。

## 操作方式

1. 啟動程式並按「選取區域」。
2. 拖曳滑鼠框選，按 `Enter` 確認；按 `Esc` 取消。
3. 視需要調整黑色容差與觸發比例。
4. 按「開始監看」。最小化視窗後程式會留在系統匣。
5. 區域連續三次達到非黑比例時會響一次；維持非黑期間不會重複響。

關閉主視窗會直接退出程式。下次啟動會載入上次設定，但不會自動開始監看。
