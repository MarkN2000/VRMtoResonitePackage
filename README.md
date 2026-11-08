# ResoPon Fork

[ResoPon](https://github.com/orange3134/VRMtoResonitePackage) から分岐した独立版です。
青を基調とするUIと `ResoPonFork.exe` で本家と区別できます。
本家への統合は予定していません。

VRMアバターを **.resonitepackage** に変換するWindows用ツールです。
変換した resonitepackage は、Resonite のファイルブラウザやインベントリへの
ドラッグ＆ドロップでそのままインポートできます。

## 特徴

- **VRM をexeにドラッグ＆ドロップするだけ**で変換完了
- VRM 0.x / VRM 1.0 の両方に対応
- Resonite本体（FrooxEngine）のインポーターをヘッドレスで動かしているので、
  Resonite内で手動インポートしたときと同じ品質のモデルが得られる
- さらにVRMの正確なメタデータを使って、ゲーム内の手動セットアップを超える自動化:
  - **ヒューマノイドリグ**: VRMのhumanoidボーンマップから正確にBipedRigを構築（名前推測に頼らない）
  - **視線・まばたき**: VRMのexpression（blink/blinkLeft/blinkRight）から目のセットアップ
    EyeRotationDriverのMaxSwingを自動調整
  - **視点位置**: 目ボーンの中点から眉間へ自動オフセットし、必要なら前方のメッシュ表面まで補正
  - **アバター保護**: SimpleAvatarProtectionを既定で付与
  - **AvatarRenderSettings**: 前髪が一人称視点を遮る問題を回避
  - **手の向き**: ボーン位置から決定論的に計算（+Z=中指方向、+Y=手の甲）
  - **リップシンク**: VRMのExpressionを元に設定。
  - **揺れもの**: VRMスプリングボーン → DynamicBoneChain変換（コライダー込み）
  - **マテリアル**: MToonのマテリアル設定を極力忠実にXiexeToonに反映。

### 表示制御オプション

- **一人称で一部メッシュを非表示にする**（既定: オフ）: VRM FirstPerson 注釈を
  `RenderMaterialOverride` と `modular_avatar/AvatarWornLocal` で反映します。
  Modular Avatar 側の不具合の影響を避けるため、必要な場合だけ有効にしてください。
  CLI では `--first-person-visibility` で有効にできます。
- **読み込み中にアイコンを表示する**（既定: オン）:
  `MeshRendererLoadStatus`、`modular_avatar/MeshNotLoaded`、各 Renderer の有効状態ドライバーと
  `Avatar Loading Display` を生成します。CLI では `--no-avatar-loading-display` で無効にできます。

2つのオプションを両方オフにした場合、FirstPerson 用の `RenderMaterialOverride`、
`Invisible Material`、headless mesh、Loading Display、読み込み状態コンポーネント、
`Avatar Root Identification` は出力されません。揺れものがある場合の DynamicBone 用
`modular_avatar` 設定、`DefaultUserScale`、Avatar Grab & Equip 修正は各機能の条件に従って残ります。

### 視点位置オプション

- **メッシュの表面に合わせて視点を前に移動する**（既定: オン）: 現在の自動計算または
  `視点の前方オフセット` / `視点の上方オフセット` で求めた位置からアバター正面へ判定し、
  アクティブかつ有効な全 `MeshRenderer` / `SkinnedMeshRenderer` のうち最も近い三角形面まで
  視点を前へ移動します。面との重なりを避けるため、交点からさらに2mm前へ配置します。
  マテリアルの透明度は判定せず、前方に交点がない場合は元の位置を維持します。
  CLI では `--no-view-surface-correction` で無効にできます。

この補正は変換時の位置計算だけで完結し、出力へ専用スロット、コンポーネント、マテリアルを
追加しません。

### 生成アセットの整理

出力パッケージの `Assets` スロットには、書き出し前に次のフォルダを作成します。

```text
Assets
├─ Meshes
├─ Materials
└─ Textures
```

`StaticMesh` を含むスロットは `Meshes`、Material のアセットプロバイダーを含むスロットは
`Materials`、`StaticTexture2D` を含むスロットは `Textures` へ移動します。Thumbnail、ShadowRamp、
FirstPerson 用の複製メッシュや Invisible Material も同じ規則で整理します。
複数カテゴリまたは未分類のアセットを同じスロット階層に含む場合は、誤分類を避けるため
`Assets` 直下に残します。スロットの親だけを変更するため、Renderer や Material の参照は維持されます。

## 必要環境

- Windows + .NET 10 ランタイム
- **Resonite がインストールされていること**

Resonite **2026.11.6.1136** のDLLでビルドと既存 `.resonitepackage` の読み込みを確認しています。

## 使い方

### ドラッグ＆ドロップ

`ResoPonFork.exe` を起動して `.vrm` をドロップしてください。

### アップデート

配布用の単一ファイルEXEは、起動時に
[このforkの最新リリース](https://github.com/MarkN2000/VRMtoResonitePackage/releases/latest)
を確認します。新しい版がある場合は確認画面を表示し、承認後にダウンロード、
SHA-256の検証、EXEの差し替え、再起動を行います。
本家のリリースは確認しません。設定の「起動時にResoPon Forkのアップデートを確認する」で
無効にできます。通信に失敗した場合や、リリースがまだない場合も変換は使用できます。
通常の `dotnet build` で生成したEXEやCLI起動では更新を確認しません。

### Resoniteのパス指定

自動検出できない場合は、次のいずれかで指定できます。

1. `--resonite-path "D:\Games\Resonite"`
2. 環境変数 `RESONITE_PATH`
3. exeと同じフォルダに `resonite-path.txt` を置いてパスを1行書く

## ビルド方法

```
dotnet build src/VrmToResonitePackage -c Release
```

配布用:

```
.\publish.ps1
```

Resonite DLLの参照解決には、ビルド時に `ResonitePath` プロパティ
（または環境変数 `RESONITE_PATH`）を使います。標準のSteamパス以外に
インストールしている場合:

```
dotnet build src/VrmToResonitePackage -c Release -p:ResonitePath="D:\Games\Resonite"
```

### UIの言語を追加する

UIの翻訳は `src/VrmToResonitePackage/Locales/` に言語ごとのJSONファイルとして分離されています。
新しい言語を追加するには、`en.json` をコピーして一意なBCP 47言語コード（例: `fr`、`pt-BR`）、
その言語での表示名、各文字列の翻訳を記述してください。追加したファイルはビルド時に自動で埋め込まれ、
設定画面の言語一覧に表示されます。翻訳がないキーは英語で表示されます。

## 互換性ワークアラウンド

Resonite（FrooxEngine）のインポーターには特定のVRMでクラッシュする既知の問題があり、
変換前にGLBを自動修正して回避しています：

- **モーフターゲット名の欠落**（旧UniVRM/UniGLTF-1.28等）: ターゲット名がprimitive側の
  extrasにしかないとAssimpが読めず全シェイプ名が空になり、FrooxEngineの重複名チェックが
  空名を素通しして「BlendShape with given key already exists」でクラッシュ
  → mesh.extras.targetNamesへ昇格・補完
- **プリミティブ間のモーフ属性不一致**（メッシュ結合ツール使用モデル等）: 同一メッシュ内の
  プリミティブ間でモーフターゲットのNORMAL有無が混在するとマージ処理が範囲外アクセスで
  クラッシュ → 不一致ターゲットからNORMAL/TANGENTを除去して整合させる
- **インポートのハング**: インポートコルーチンが例外で死ぬと完了通知が来ないため、
  タイムアウト（`--import-timeout`、既定300秒）で検出して次のファイルへ進む

## 仕組み

1. VRM（= glTFバイナリ）のJSONチャンクを自前でパースし、humanoid/expressions/
   springBones/MToonなどのVRM固有データを抽出
2. FrooxEngineを `StandaloneFrooxEngineRunner` でヘッドレス起動し、一時ワールドを作成
3. `.vrm` を `.glb` としてResonite標準の `ModelImporter`（XiexeToonプリセット）でインポート
4. VRMデータを使ってアバターセットアップ（ゲーム内AvatarCreatorの処理をヘッドレスで再現）
5. `Slot.SaveObject` + `PackageCreator.BuildPackage` で `.resonitepackage` を出力

## データの保存場所

このツールはエンジン起動時に`-DataPath`/`-CachePath`相当の指定を行い、
エンジンのデータを専用フォルダ `%LOCALAPPDATA%\ResoPonFork\` 配下に生成します：

- `Data\` — エンジンのLocalDB（アセットキャッシュ）。破損時は自動でリセットされます
- `Cache\` — エンジンのキャッシュ
- `Updates\` — ダウンロードしたアップデートの一時保存先

設定は `%APPDATA%\ResoPonFork\settings.json`、変換ログはEXEと同じフォルダの
`Logs\` に保存します。本家の設定・キャッシュとは分離しています。

**Resonite本体のデータフォルダには一切触れません**（プレイ環境に影響しません）。
自動リセット処理にも、このフォルダ外では動作しないガードが入っています。
ツールの動作がおかしくなった場合は`%LOCALAPPDATA%\ResoPonFork`を
丸ごと削除しても安全です。

## 制限事項

- MToon/liltoonの完全再現はできません（ResoniteのXiexeToonへの近似マッピング）
- VRMの表情（happy/angry等）のフェイストラッキング連動は`--face-tracking`指定時のみ、
  かつ名前ヒューリスティクスによる割り当てです
- VRM 1.0のコンストレイント（VRMC_node_constraint）は未対応
