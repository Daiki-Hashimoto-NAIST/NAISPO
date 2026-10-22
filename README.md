# NAISPO

## ふたりで コインラリー

このブランチ `codex/cooperative-coin-rally` には、2人で低いネットを越えてボールを打ち合い、放物線状に並ぶコインを集める協力モードがあります。Kinect 2台と、カメラ不要の2人用キーボード操作に対応します。

**起動：`Run-CooperativeRally.cmd`**。Unityでは `Naispo > Cooperative Rally > Open` を選びます。操作・カメラ設定・ビルド方法は [協力モードのガイド](COOPERATIVE_RALLY.md) にあります。

## 元の1台用ゲーム

Azure Kinect DKで両手の動きを読み取り、手と手の間に表示されるバリアでボールを打ち返すUnityのゲームです。

元の `codex/light-coin-game` のシーンでは、重力で落ちるボールを打ち上げてコインを集めます。ボールの周囲だけカメラ映像が見え、その外側には暗いシルエットが残ります。黄色いコインも、ボールの光に照らされて初めて見えます。

## 遊び方

両手を広げると、その間にバリアが表示されます。バリアを傾けてボールの飛ぶ方向を変え、コインに当ててください。手幅を狭くすると強く打ち返せます。広くすると反発が弱まり、バリアが大きくたわみます。

コイン6個を集めると次のラウンドへ進みます。ボールが画面下へ落ちた場合は、0.8秒後に中央上から再登場します。獲得済みのコインは失いません。

| 画面のボタン | 操作 |
|---|---|
| `Ball light: ON/OFF` | ボールの照明を切り替えます。OFFの間はコインも表示・取得されません。 |
| `Mode: Hand walls` | 両手バリアと、体のシルエットでボールを押す方式を切り替えます。 |
| `Restart game` | スコアとコインの配置を初期化します。 |

## 起動方法

Windows x64、Azure Kinect DK、Unity 6000.6.5f1、Git LFSを使います。

1. リポジトリを取得し、`codex/light-coin-game` ブランチに切り替えます。
2. リポジトリ内で `git lfs pull` を実行します。人体認識モデルが約167MBの実ファイルになっていることを確認してください。
3. Unityでプロジェクトを開き、`Naispo > Build Windows Demo` を選びます。`Builds/Windows/Naispo.exe` が作成されます。
4. Kinectに電源とUSBを接続し、背面ランプの白点灯を確認します。Kinect Viewerなど、同じセンサーを使うアプリは終了してください。
5. リポジトリ直下の `Run-Naispo.cmd` を実行します。

起動スクリプトはビルドを `%USERPROFILE%/NaispoRuntime` にコピーして実行します。日本語を含むパスでSensor SDKのエラーが出た環境への対処です。実行先のパスは英数字にしてください。起動ログは同じフォルダーの `Player.log` に保存されます。

SDKのDLLと人体認識モデルはプロジェクトに含まれます。モデルの取得にはGit LFSが必要です。ビルド済みの実行ファイルはリポジトリに含めていません。

## 設定と資料

- [コインゲームのルールと調整値](LIGHT_COIN_GAME.md)
- [ボール照明の仕組みと設定](BALL_LIGHT.md)
- [弾性バリアの仕組みと設定](ELASTIC_BARRIER.md)

`KINECT_SETUP.md`、`KINECT_PROGRESS.md`、`Documentation/VALIDATION.md` は元のシルエットデモの資料です。起動方法やゲームのルールは、このREADMEと上記の資料を参照してください。

照明だけの試作は `codex/ball-light-reveal` ブランチに残しています。

## 動作確認

2026年10月16日、Unity 6000.6.5f1で28件のテストとWindowsビルドが成功しました。実機では1人の追跡とカラー映像の取得が約30 FPSで動作し、バリアでボールを打ち上げてコインを獲得できることを確認しています。複数人での操作感と長時間のプレイは未確認です。

SDKのライセンスは `Documentation/Licenses`、同梱ファイルの記録は `Documentation/SDK_COPY_MANIFEST.json` にあります。
