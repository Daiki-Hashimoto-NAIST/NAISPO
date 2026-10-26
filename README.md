# NAISPO

両手の間に張ったバリアでボールを打ち返す、2人用のUnityゲームです。Azure Kinect DKを1人1台使う入力と、カメラ不要のキーボード操作に対応します。

## 2つのモード

- **星あつめ**：魔法の温室で、ボールの光を頼りに隠れた星9個を2人で集めます。集めるたびに周囲のつぼみに命が灯り、全部集めると花が開きます。ボールを落としても取得済みの星は失いません。
- **対戦**：同じ世界の魔法の決闘場で、相手側の床にボールを落とすと1点、3点先取で勝ちです。火・時計・ランタンの魔法で、球の速さや見え方が変わります。

右下の設定アイコン、または左下のモード名から切り替えられます。詳しいルールと調整値は [ふたりのラリーのガイド](COOPERATIVE_RALLY.md) を参照してください。

## 遊び方

バリアを傾けて返球方向を変え、手幅を狭くすると強く、広くすると弱く返せます。ボールはバリアに少し沈み込んでから跳ね返り、押し返す方向への手の動きも加速に反映します。

最初はキーボード操作の「DEMO」で起動します。

| 操作 | 左の人 | 右の人 |
|---|---|---|
| 移動 | W / A / S / D | 矢印キー |
| バリアを傾ける | Q / E | J / L |
| 手幅を変える | Z / X | U / O |

各自のコートをマウスでドラッグしても位置を動かせます。Kinectを使う場合は、設定画面の「カメラ」で「自動設定」を選び、「保存してKinectを開始」を押します。設置方法と接続診断は [Kinect 2台で遊ぶ](COOPERATIVE_RALLY.md#kinect-2台で遊ぶ) を参照してください。

## 起動方法

Windows x64、Unity 6000.6.5f1、Git LFS、.NET 10 SDKを使用します。ビルド済みの実行ファイルはリポジトリに含めていません。

1. 使用するブランチを取得し、リポジトリ内で `git lfs pull` を実行します。SDKのDLLと人体認識モデルを取得します。
2. PowerShellで `./Tools/KinectWorker/Build.ps1` を実行し、Kinect用のWorkerをビルドします。.NET SDKの場所を指定する場合は `-DotNetPath "path/to/dotnet.exe"` を追加します。
3. Unityでプロジェクトを開き、`Naispo > Cooperative Rally > Build Windows` を選びます。
4. リポジトリ直下の **`Run-CooperativeRally.cmd`** を実行します。

Unity Editorで遊ぶ場合は `Naispo > Cooperative Rally > Open` で専用シーンを開き、Playを押します。通常のBuild Settingsには旧1台版のシーンが残っているため、2人用のビルドには上記の専用メニューを使います。

生成先は `Builds/CooperativeRally` です。配布する場合はこのフォルダー全体が必要です。Workerの.NETランタイムも含まれるため、配布先PCへの.NET SDKの導入は不要です。

起動スクリプトはビルドを `%USERPROFILE%/NaispoCoopRuntime` にコピーして実行します。日本語を含む実行パスで発生したKinect SDKの問題を避けるため、実行先は英数字のパスにしてください。起動ログは同じフォルダーの `Player.log` に保存されます。

更新時はWorkerとWindows版の両方を再ビルドしてください。Gitの更新だけでは、既存の実行ファイルは変わりません。

## 元の1台用ゲーム

光で照らしたコイン6個を集める旧1台版も、同じリポジトリに残っています。ブランチの切り替えは不要です。

- Editorで開く：`Naispo > Open Kinect Demo`
- ビルド：`Naispo > Build Windows Demo`
- 実行：`Run-Naispo.cmd`（生成先は `Builds/Windows`、実行先は `%USERPROFILE%/NaispoRuntime`）

ルールと画面の操作は [旧コインゲームのガイド](LIGHT_COIN_GAME.md) を参照してください。

## 設定と資料

- [2人用のルール・操作・カメラ設定](COOPERATIVE_RALLY.md)
- [2人用の検証記録](Documentation/Validation/cooperative-rally.md)
- [魔法テーマの画像素材と差し替え方](Documentation/Art/magical-rally-assets.md)
- [旧コインゲームのルールと調整値](LIGHT_COIN_GAME.md)
- [ボール照明の仕組みと設定](BALL_LIGHT.md)
- [弾性バリアの仕組みと設定](ELASTIC_BARRIER.md)

`KINECT_SETUP.md`、`KINECT_PROGRESS.md`、`Documentation/VALIDATION.md` は元のシルエットデモの資料です。起動方法やゲームのルールは、このREADMEと上記の資料を参照してください。

照明だけの試作は `codex/ball-light-reveal` ブランチに残しています。

## 動作確認

2026年10月27日、魔法テーマと3種類のアイテムを含む全201件のテストが成功しました。最終Windowsビルドとデモでのモード切り替え・起動・終了も確認しています。実施したテストと画面確認の結果は [検証記録](Documentation/Validation/cooperative-rally.md) にあります。

この版でのKinect実機2台・2人プレイ、最新修正の操作感、長時間の安定性は未確認です。旧1台版で確認した実機動作の記録とは区別しています。

SDKのライセンスは `Documentation/Licenses`、同梱ファイルの記録は `Documentation/SDK_COPY_MANIFEST.json` にあります。
