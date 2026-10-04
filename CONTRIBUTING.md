# Development

.NET 10 SDKが必要です。変更前後に`dotnet test TSimulator.slnx -c Release`を実行してください。

- ルールの変更は`docs/rules.md`の項目番号をPRに記載し、境界条件をテストします。
- 描画だけの変更は`--smoke`のスクリーンショットと、実際の操作で確認します。キー変更、リサイズ、設定保存、CADがない状態も確認してください。
- 日本語UIを変更したら`python scripts/update-ui-glyphs.py`で静的グリフ一覧を更新します。モジュール・プリセットの文字は実行時に追加します。UI操作の回帰確認は`tests/ui/*.rae`を`--smoke ... --automation FILE --user-data artifacts/test-user`で実行できます。通常の利用者の設定を使わず、保存結果を`scripts/verify-ui-flow.py`で確認します。
- バージョンは`Directory.Build.props`と`CHANGELOG.md`を更新します。`v0.2.0`などのタグでリリースします。
- 原文の無断複製や、利用者の設定・ログをGitに追加しないでください。公式CADは自由利用の許可確認に基づき同梱しています。追加する素材には出典・利用許可を記録してください。
- CIのOS別パッケージをダウンロードし、対象OSで起動・操作・終了できることを確認します。Macの署名を導入する場合は証明書・公証情報をActions Secretsで管理します。

新しいロボット制御は`IRobotController`へ実装します。競技値はRuleProfile、実測値はRobotSpec/PhysicsSettingsへ追加し、描画コードへルールを埋め込まないでください。
