# ふたりで コインラリー

2人でバリアを傾け、低いネットを越えてボールを打ち合う協力ゲームです。画面中央のコイン9枚を集めると、次のラウンドへ進みます。

コインは放物線状に並び、ボールの光が届く範囲だけ見えます。光るボールを返しながら、隠れたコインを探して集めます。スコアは2人で共有します。ボールを落としても取得済みのコインは残り、左右交互に次のボールが出ます。

## 起動

Windowsのビルドがある場合は、リポジトリ直下の `Run-CooperativeRally.cmd` を実行します。最初はカメラを使わないキーボードモードで起動します。

Unityでは `Naispo > Cooperative Rally > Open` から専用シーンを開いて再生します。シーンは `Assets/Scenes/CooperativeCoinRally.unity` です。元の1台版は `Naispo > Open Kinect Demo` で開けます。

## キーボードで遊ぶ

| 操作 | 左の人 | 右の人 |
|---|---|---|
| 移動 | W / A / S / D | 矢印キー |
| バリアを傾ける | Q / E | J / L |
| 手幅を変える | Z / X | U / O |

各プレイヤー側の画面をマウスでドラッグしても位置を動かせます。手幅を狭めると強く返せます。ボールの速さとネットの高さは上部の設定から調整できます。「ボールの光」は初期状態でONです。OFFにするとコインは見えず、取れなくなります。

## Kinect 2台で遊ぶ

1. Azure Kinect DKを2台とも電源とUSB 3へ接続します。同じカメラを使うViewerなどは終了します。
2. 2人が横に並んで同じ画面を見られるようにし、各カメラが1人ずつ正面から撮る位置に置きます。
3. ゲーム画面下の「カメラの設定」で、左・右それぞれのカメラ本体のシリアル番号を確認します。以前の機器の番号が初期値として入っています。左右が逆なら「左右を入れ替える」を使います。
4. 検出する距離を設置位置に合わせ、「保存してKinectを開始」を押します。設定はこのPCに保存されます。
5. 2人とも両手が認識されるとボールが動きます。どちらかの認識が途切れると一時停止し、復帰後に続きます。

カメラごとに人体認識を実行し、左右のコートへ割り当てます。既定は15fps、NFOV 2x2 Binned、DirectMLです。撮影範囲が重なりすぎないように設置してください。カメラ間のハードウェア同期や3D空間の合成は行いません。

## ビルド

Unity 6000.6.5f1、Windows x64、Git LFSを使用します。

1. `git lfs pull` でモデルとSDK DLLを取得します。
2. .NET 10 SDKで `Tools/KinectWorker/Build.ps1` を実行します。SDKの場所を指定する場合は `-DotNetPath "path/to/dotnet.exe"` を追加します。Workerとその実行用.NETランタイムをローカルに生成します。
3. Unityで `Naispo > Cooperative Rally > Build Windows` を選びます。
4. `Run-CooperativeRally.cmd` を実行します。

生成先は `Builds/CooperativeRally` です。Worker・.NETランタイム・日本語フォントのライセンスもビルドへコピーされます。配布時はこのフォルダー全体が必要です。

起動スクリプトは `%USERPROFILE%/NaispoCoopRuntime` にビルドをコピーして起動します。日本語の実行パスで発生したKinect SDKの問題を避けるためです。ログは同フォルダーの `Player.log` にあります。

## 構成

- `CooperativeCoinGame`：共通ボール、ネット、コイン、スコア、ラウンド。
- `CooperativeRallyApp`：左右の入力・表示、キーボードとKinectの切り替え、カメラ設定。
- `CooperativeDemoSource`：キーボード操作をKinectと共通の手位置データへ変換。
- `MultiKinect` と `Tools/KinectWorker`：カメラごとの別プロセスで人体認識し、最新フレームをUnityへ渡す。

元の照明・コインゲームは [LIGHT_COIN_GAME.md](LIGHT_COIN_GAME.md)、弾性バリアは [ELASTIC_BARRIER.md](ELASTIC_BARRIER.md) を参照してください。
