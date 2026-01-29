# Shogun.Avalonia

multi-agent-shogun 用の GUI アプリ（Avalonia）。フォーク元と同一の「将軍→家老→足軽」のロジックをアプリ内で完結する。

## 主な動作

1. **送信**: 入力欄で指示を入力して送信すると、`queue/shogun_to_karo.yaml` に cmd_xxx として追加され、アプリ内のオーケストレーターが家老（Claude API）→タスク分解→足軽（Claude API）→報告→`dashboard.md` 更新まで一括実行する。
2. **ダッシュボード**: 「ダッシュボード」タブで `dashboard.md` の内容を表示。「更新」で再読み込み。送信後も自動で更新する。
3. **エージェント表示**: 「エージェント」タブで家老・足軽のキュー/タスク/報告を「更新」で反映する（家老＝shogun_to_karo の要約、足軽 N＝queue/tasks/ashigaruN.yaml と queue/reports/ashigaruN_report.yaml）。

## 設定

- **ワークスペースルート**: 設定画面の「ワークスペースルート」に、queue/dashboard/instructions の親フォルダ（multi-agent-shogun のフォルダ等）を指定する。空のときは config の親を参照する。
- **Claude Code 環境**: 設定画面の「Claude Code 環境」で、**アプリ内に Node.js と Claude Code CLI をインストール**する（環境を汚さない。`%LocalAppData%\Shogun.Avalonia` に配置）。「Node.js をインストール」→「Claude Code をインストール」の順で実行する。
- **API 呼び出し**: 当アプリでは **API キー（環境変数含む）は一切使用しない**。家老・足軽の Claude API 呼び出しはアプリからは行わない。キューへの書き込み・ダッシュボード表示と、上記アプリ内 Claude Code CLI の導入までを担当。詳細は `docs/CLAUDE_CODE_INTEGRATION.md` を参照。
- **memory/global_context.md**: ワークスペースルート直下の `memory/global_context.md` が存在する場合、家老・足軽のプロンプトに「システム全体の設定・殿の好み」として先頭に付与する。フォーク元のコンテキスト読み込み手順に準拠。
- **status/master_status.yaml**: 送信実行の開始・完了・失敗時に `status/master_status.yaml` を書き込む（フォーク元の shutsujin_departure.sh と同形式の全体進捗）。他ツールやスクリプトが参照する想定。
- **ログレベル (logging.level)**: 設定画面の「ログレベル」は `config/settings.yaml` の `logging.level` に保存される。upstream の CLI・スクリプト（tmux セッション等）がこの YAML を参照する想定。**本アプリ（Shogun.Avalonia）の Logger はこの設定を読まず**、ビルド時に固定されたレベル（DEBUG ビルドでは Debug、それ以外では Info）でログ出力する。

## フォーク運用上の注意

- **参照・起動してよいもの**: `config/projects.yaml`、`queue/`、`dashboard.md`、`status/`、`instructions/` など、YAML/マークダウンで定義されているデータや設定。これらは当アプリで読み書きしてよい。
- **編集しないこと**: フォーク元のコマンド用スクリプト（`shutsujin_departure.sh`、`first_setup.sh`、`install.bat`、`instructions/*` など）。これらは「参考」とし、スクリプト本体の変更やパッチは当方では行わない。フォーク元の更新で競合を避けるため（ルートの `FORK_POLICY.md` 参照）。

当方では CLI 起動は行わず、アプリ（Shogun.Avalonia）のみで完結する想定。
