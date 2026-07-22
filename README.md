# Color Alert

Color Alert 是 Windows 10/11 的輕量桌面工具。使用者可以從畫面選取一個目標顏色，再框選要監看的區域；當該區域穩定偏離目標顏色時，程式會播放 Windows 系統提示音，恢復目標顏色後才重新待命。

## 功能

- 支援多螢幕、負座標與不同 DPI 的矩形選區
- 使用跨螢幕滴管精確選取目標顏色
- 以單一「敏感度」控制顏色與範圍變化的觸發程度
- 連續三次取樣確認，並以一半觸發比例作為復歸門檻
- 可選擇播放一聲或連響三聲
- 可顯示位於擷取範圍外、不擋滑鼠的監看框線
- 最小化或關閉至系統匣，可從選單開始、暫停、重選或真正退出
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
3. 按「從畫面取色」，再點擊要作為基準的畫面顏色。
4. 視需要調整敏感度、提示音次數與是否顯示監看區域。
5. 按「開始監看」。最小化或關閉視窗後程式會留在系統匣。
6. 區域連續三次達到變化門檻時會提示；維持變化期間不會重複響。

只有系統匣選單的「退出」會真正結束程式。下次啟動會載入上次設定，但不會自動開始監看。
