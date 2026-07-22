# Color Alert

Color Alert 是 Windows 10/11 的輕量桌面工具。使用者可以框選最多 10 個監看區域；程式會以匡選當下的畫面作為各區基準，當任一區域穩定出現變化時播放 Windows 系統提示音，該區恢復基準後才重新待命。

## 功能

- 最多同時監看 10 個區域，支援多螢幕、負座標與不同 DPI
- 每個區域獨立比較匡選時的基準畫面、獨立觸發與重新待命
- 以單一「敏感度」控制像素差異與變化範圍的觸發程度
- 同一取樣週期有多區同時觸發時，只播放一次提示音序列
- 連續三次取樣確認，並以一半觸發比例作為復歸門檻
- 可選擇播放一聲或連響三聲
- 可顯示位於擷取範圍外、不擋滑鼠的多區監看框線；已觸發區域顯示橘色
- 最小化或關閉至系統匣，可從選單開始、暫停、新增區域或真正退出
- 區域座標與一般設定保存在 `%LocalAppData%\ColorAlert\settings.json`
- 基準畫面只保存在記憶體；不連網、不要求管理員權限、不保存螢幕影像

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

1. 啟動程式並按「新增區域」。
2. 拖曳滑鼠框選，按 `Enter` 確認；按 `Esc` 取消。
3. 視需要繼續新增其他區域，或用每列按鈕更新基準、重新選取或刪除。
4. 視需要調整敏感度、提示音次數與是否顯示監看區域。
5. 按「開始監看」。重新啟動程式後會在第一次開始時重新取得所有基準；同次執行中的暫停／恢復會沿用原基準。
6. 任一區域連續三次達到變化門檻時會提示；維持變化期間不會重複響，其他區域稍後變化仍可獨立提示。

只有系統匣選單的「退出」會真正結束程式。下次啟動會載入上次設定，但不會自動開始監看。
