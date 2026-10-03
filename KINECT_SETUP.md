# Azure Kinect シルエットデモ

## 起動

1. Azure Kinect DK に電源と USB 3 を接続し、Kinect Viewer 等を終了する。
2. Unity 6000.6.4f1 で `Assets/Scenes/KinectSilhouetteDemo.unity` を開く。
3. Play を押す。左上に `Tracker ready: DirectML` が表示される。
4. センサーを横向きに置き、約2.5～4m前に立つ。人体に触れた白いボールが移動し、外周で反射する。
5. 終了は Play 停止。未接続時は警告を表示して取得スレッドを終了する。接続を直したら KinectSystem の Source コンポーネントを再度有効化するか Play を再開する。

## 実際に使用した SDK と DLL

- Sensor SDK **1.4.2**: `C:\Program Files\Azure Kinect SDK v1.4.2`
- Body Tracking SDK **1.1.2**: `C:\Program Files\Azure Kinect Body Tracking SDK`
- URP **17.6.0** / Unity Pipeline **0.8.0-exp.1** / Unity CLI **1.0.0-beta.12**。
- SDK/Unity/パッケージのバージョン変更はしていない。

| 配置 | ファイル |
|---|---|
| `Assets/Plugins/AzureKinect/Managed` | `Microsoft.Azure.Kinect.Sensor.dll`, `Microsoft.Azure.Kinect.BodyTracking.dll` |
| `Assets/Plugins/AzureKinect/x86_64` | `k4a.dll`, `depthengine_2_0.dll`, `k4abt.dll`, `directml.dll`, `onnxruntime.dll`, `onnxruntime_providers_shared.dll` |
| `Assets/StreamingAssets/AzureKinect` | `dnn_model_2_0_op11.onnx` |

コピー元・サイズ・SHA256 は `Documentation/SDK_COPY_MANIFEST.json`。ライセンスは `Documentation/Licenses`。Native plugin は Windows x64 / Windows Editor 専用、Managed plugin は Windows Editor / Win64 に設定済み。同名 System.* DLL の追加は不要（Unity の既存実装を使用）。`k4arecord.dll`、CUDA/TensorRT の大容量依存は使用していない。システム SDK と PATH は変更していない。

公式 managed DLL を調査して `TrackerProcessingMode.DirectML`、`TrackerConfiguration.ModelPath`、`Frame.GetBodyId`、`Image.Memory` を使用。スムージング API の正確な綴りは **`SetTemporalSmooting`**。Body Tracking DLL 自体の AssemblyVersion は 1.0.0.0 だが、インストール済み SDK の header は 1.1.2。

## Inspector の設定

`Assets/Settings/KinectSettings.asset` を選択する。

| 設定 | 初期値 / 意味 |
|---|---|
| Depth Mode | `NFOV_2x2Binned`（320×288）。NFOV_Unbinned / WFOV_2x2Binned 等も選択可能 |
| Camera FPS | `FPS30`。WFOV_Unbinned は最大15fps。Color は OFF |
| Processing Mode | `DirectML`。GPU 初期化に失敗した時のみ、明示ログ後 CPU fallback |
| Skeleton Smoothing | 0.2（輪郭画像にかける平滑化ではなく SDK の skeleton 設定） |
| Model File | StreamingAssets からの相対パス。絶対パスの override も可能 |
| Mirror X | true。描画と Collider は共通 Mapper で反転 |
| Maximum Body Count | 3。フレーム内 index → 永続 Body ID → 色スロットで管理 |
| Person Colors | Cyan / Magenta / Yellow |
| Body Lost Timeout | 0.75秒で色スロット解放。姿を失った Collider は直ちに無効化 |
| Stale Frame Timeout | 0.5秒。取得停止時は表示と当たり判定を無効化 |
| Minimum Component Area | 40 pixel。小さな孤立成分を無視 |
| Noise Neighbor Threshold | 2。周囲8近傍の同一人物ピクセル数で孤立ノイズを抑制 |
| Contour Epsilon | 2.5 pixel。大きくすると滑らか・軽くなり、細部を失う |
| Maximum Contour Points | 192点 / 人、全 Path の合計を制限 |
| Maximum Components Per Body | 4。大きい成分を優先し、穴は Collider では埋める |
| Collider Update Rate | 25Hz。描画は取得フレームに追従（通常30fps） |
| Debug Logging / Overlay | 概要を5秒ごとに更新。Profiler markers も利用可能 |

Capture、FPS、Processing Mode、Smoothing、モデル設定を変更した後は Play を再開する。最初の検証対象は DirectML と CPU。CUDA/TensorRT 用の依存 DLL は同梱していない。

物理の調整は `Assets/Materials/AirHockey.physicsMaterial2D`（Friction 0 / Bounciness 0.9）と Ball の Inspector。最大速度12、Gravity Scale 0、Continuous、Interpolate。壁は Orthographic Camera の表示領域へ追従する。

`SilhouetteBallPush` は形状変形の速度を接触へ補う独立コンポーネント。最大表面速度6、追加速度上限3。無効化すると Unity Physics2D だけの挙動と比較できる。

## Scene と処理構成

```
Main Camera
KinectSystem               Source / Manager / Mapper
SilhouetteRenderer        再利用 Texture + Unlit の透明描画
PersonColliders
  PersonSilhouetteCollider_0..2  Kinematic + PolygonCollider2D + SurfaceMotion
Ball                      Dynamic / CircleCollider2D / Sprite / BallPush
ScreenBoundaries
  TopWall / BottomWall / LeftWall / RightWall
```

Worker が SDK の Capture / Tracker / BodyIndexMap を処理し、再利用する3つの CPU バッファで最新1フレームを共有する。Main Thread は SDK 待機をせず、Texture・GameObject・Collider だけを更新する。座標は画像左上原点の pixel edge を基準とし、Mapper が均等スケールで Aspect Fit。Camera は XY 平面に正対した Orthographic 設定を使う。

輪郭は 4-connected components → pixel-edge の外周追跡 → 閉じた輪郭の RDP → 自己交差検査。大きな分離成分は複数 Path。無効な輪郭は Inspector の rejectedContours に計上し、物理エンジンに渡さない。

## 検証と現状

詳細は `KINECT_PROGRESS.md` と `Documentation/VALIDATION.md` を参照。

- Unity CLI の再コンパイル、EditMode テスト、Play Mode の実 SDK 起動を実施。
- 実機: DirectML / 320×288 / 約30fps / 0人時の BodyIndexMap と Texture 作成。
- 人工マスク: 3色、Mirror、Aspect Fit、輪郭と複数 Path、更新性能を検証。
- Unity Physics2D: 壁の反射、Kinematic の押し出し、形状変形の補助 Impulse を検証。
- **実際の人間のシルエット、1～3人同時追跡、人体での操作感は、センサー前での確認が必要。人工マスクによる検証と区別する。**
- USB 抜線中の挙動、Standalone ビルド、別 PC、IL2CPP は未検証。現環境 Windows x64 Unity Editor での初期実装。
- Native `Tracker.Create` にはキャンセル API がない。通常停止は worker を join するが、native 初期化が5秒を超えて戻らない場合は Editor の無期限停止を避け、警告してキャンセル待ちにする。native 呼び出しを強制 Thread.Abort しない。

確認手順: 1人で左右・腕・脚を動かし、鏡の方向、輪郭と衝突位置、ボールの押しやすさを確認する。2～3人で色の安定性と人物の出入りを確認する。Game View の比率を変えて人体が伸縮しないことを確認する。Play の停止・再開を試す。

## 再開と開発

- 元プロンプト: `Documentation/KINECT_REQUIREMENTS.txt`
- 進捗・次の手順: `KINECT_PROGRESS.md`。利用制限による中断後も最初から作り直さない。
- Scene 構築補助: `Tools/CompleteKinectScene.cs`（既存デモ Scene を開き、停止中に Unity Pipeline `run_script` で実行）。
- テスト: Unity Test Runner の `Naispo.Tests`。CLI 再コンパイル例: `unity recompile --project-path <project-root> --json`。
- `Tools/ValidateSyntheticFrame.cs` / `Tools/ValidatePlayModePhysics.cs` / `Tools/ValidateCaptureLifecycle.cs` は Play Mode 用の検証補助。
- 元の SampleScene のディスク版を `Documentation/Backups` に保管した後、開いていた未保存の変更を保存し、デモへ切り替えた。

## SDF へ発展させる箇所

`AzureKinectBodySource` / `BodyFrameData` / `BodyColorRegistry` / `KinectViewportMapper` は継続利用できる。`SilhouetteContourExtractor` と `SilhouetteColliderController` を SDF 生成・接触判定へ差し替え、必要なら `SilhouetteSurfaceMotion` / `SilhouetteBallPush` の接触応答を置換する。描画と取得処理を作り直す必要はない。
