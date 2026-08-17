# Umbra AutoRetainer

Umbra のツールバーに、AutoRetainer のリテイナー／潜水艦の状態を出します。

表示例: `R2❘5　M1❘3`

- **R** = リテイナー（回収できる人数 ❘ ベンチャー中の人数）
- **M** = 潜水艦（回収できる隻数 ❘ 航海中の隻数）
- クリックで AutoRetainer の `/ays` を開きます

数字は AutoRetainer のデータを使います。ウィジェット設定の **キャラクター** タブで、数えるキャラとリテイナーを選べます。

## 必要なもの

- [Umbra](https://github.com/una-xiv/umbra)
- [AutoRetainer](https://github.com/PunishXIV/AutoRetainer)

## 入れ方

1. 下の「ビルド」を実行する
2. ゲームを起動して Umbra と AutoRetainer を有効にする
3. Umbra の設定を開く
4. **Custom Plugins（カスタムプラグイン）** を有効にする
5. **Plugins** から、次の DLL を追加する

`J:\Umbra.AutoRetainer\out\Release\Umbra.AutoRetainer.dll`

または、`install-dev.ps1` を実行したあと、次の場所を指定する。

`%APPDATA%\XIVLauncher\devPlugins\Umbra.AutoRetainer\Umbra.AutoRetainer.dll`

6. Umbra を再読み込みする
7. ツールバーの「ウィジェットを追加」から **AutoRetainer** を足す

## ビルド

PowerShell で、このフォルダを開いて実行します。

```powershell
cd J:\Umbra.AutoRetainer
.\build.ps1
.\install-dev.ps1
```

## 表示が出ないとき

- AutoRetainer が入っていないと、ウィジェット一覧に出ません
- 出ても `ARオフ` のときは、AutoRetainer を有効にして Umbra を再読み込みしてください
