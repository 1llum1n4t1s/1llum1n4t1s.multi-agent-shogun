# 当アプリで Claude Code を使うための設計

## 方針

- **upstream**: tmux + Claude Code CLI を各ペインで起動する構成。
- **当アプリ**: 環境を汚さず、**アプリ内に Node.js と Claude Code CLI を導入**する。

## アプリ内での導入

1. **Node.js**: アプリが Node.js LTS（v20）を nodejs.org からダウンロードし、`%LocalAppData%\Shogun.Avalonia\nodejs` に展開する。システムの Node は使わない。
2. **Claude Code CLI**: アプリ内の npm で `npm install -g @anthropic-ai/claude-code` を実行する。`NPM_CONFIG_PREFIX` を `%LocalAppData%\Shogun.Avalonia\npm` に設定するため、グローバルインストール先もアプリ専用になる。
3. **実行フロー**（upstream の将軍→家老→足軽→家老報告に相当）:
   - **家老**: 送信時に家老として起動。`queue/shogun_to_karo.yaml` を読んでタスク分解し、`queue/tasks/ashigaru{N}.yaml` に割り当てを書く。
   - **足軽**: 家老完了後、タスクが書かれた `ashigaru{N}.yaml` がある足軽 N ごとに Claude Code を起動。`instructions/ashigaru.md` をシステムプロンプトに、ユーザープロンプト「queue/tasks/ashigaru{N}.yaml に任務がある。確認して実行せよ。」で実行。複数足軽は並列実行。完了時に `queue/reports/ashigaru{N}_report.yaml` を書く。
   - **家老（報告集約）**: 全足軽完了後、家老を再度起動。ユーザープロンプト「queue/reports/ の報告を確認し、dashboard.md の「戦果」を更新せよ。」で、報告スキャンと dashboard.md 更新を行う。

## 起動時の確認・自動インストールの順序

依存関係に基づく**正しい実行順**は次のとおり（逆順では成立しない）。

1. **Node.js の確認** → 未インストールなら自動インストール  
   （Claude Code は npm で入れるため、Node が無いと次に進めない）
2. **Claude Code の確認** → 未インストール、またはこの起動で Node を入れた場合は自動インストール  
   （ログイン確認は `claude` コマンドが必要なため、Claude Code が無いと次に進めない）
3. **ログイン確認** → テストコマンド（`claude -p "..."`）でログイン済みか判定

※ 当初の「①ログイン ②Claude Code ③Node」は確認の優先度であり、実行順は上記の通り。

## 設定画面

- 「Claude Code 環境」セクションで、Node.js と Claude Code CLI のインストール状態を表示。
- 「Node.js をインストール」「Claude Code をインストール」ボタンで、上記の導入を実行する。

## まとめ

| 項目 | upstream | 当アプリ |
|------|----------|----------|
| Node.js | ユーザーが環境にインストール | アプリがアプリ用フォルダに導入 |
| Claude Code | npm -g でユーザー環境にインストール | npm -g を prefix 付きでアプリ用にインストール |

これにより、**環境を汚さず、アプリ内だけで Node.js と Claude Code CLI を利用**できる。
