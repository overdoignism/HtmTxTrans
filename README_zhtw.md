# HtmTxTrans

For English version, please see [README.md](README.md)

## 1. 程式用途與概念

**HtmTxTrans** 是一個專為對接大型語言模型 (LLM) 所設計的強大 CLI 工具，旨在進行 HTML 檔案的全文拆解與批次翻譯。

直接讓 LLM 翻譯包含 HTML 標籤的文本，經常會導致標籤損毀、屬性遺失或結構幻覺。HtmTxTrans 透過「拆解 - 翻譯 - 重組」的管線概念解決了這個痛點。它能精準抽出 HTML 中的文字與屬性，將純文字結合上下文進行翻譯，最後再將原始的 HTML 標籤無縫且精準地映射回翻譯後的文本中。

## 2. 開發背景與技術棧

- **開發框架：** .NET 8

- **核心套件：**
  
  - OpenAI (2.10.0, 官方 .NET SDK，用於標準化的 LLM API 溝通, MIT授權)
  
  - YamlDotNet (17.0.1, 用於高可讀性的設定檔、提示詞管理，以及嚴謹的狀態儲存, MIT授權)
  
  - AngleSharp (1.4.0, 用於解析與操作 HTML DOM 結構, MIT授權)

- **開源授權：** 本專案採用 MIT License 授權條款。

## 3. 工作流程 (6 個階段)

HtmTxTrans 採用了具備高度容錯能力的 6 階段工作管線。每個階段都會將狀態儲存為人類可讀的 YAML 檔案，極大化了除錯、接續執行與獨立重試的便利性。

- **Pass 1: 節點提取與分割 (HTML Extraction & Chunking)**  
  使用 AngleSharp 抽出文字與指定屬性。剝離行內標籤，並透過滑動視窗與 LLM 句首邊界判定，將文本分割為最佳大小的區塊。

- **Pass 2: 專有名詞提取與詞彙表建立 (Proper Noun Extraction)**  
  掃描文本區塊以識別適合音譯的專有名詞，建立滾動式的雙語詞彙表以確保術語一致性。

- **Pass 3: 上下文感知翻譯 (Context-Aware Translation)**  
  使用生成的詞彙表翻譯純文字區塊，並將前後文傳遞給 LLM 以進行高精準度的在地化翻譯。

- **Pass 4: 節點對齊 (Node Alignment)**  
  執行 YAML 原地更新，將翻譯後的純文字片段映射回原始帶有 ID 的 Node 結構中。

- **Pass 5: 標籤對齊 (Tag Alignment)**  
  將剝離的 HTML 行內標籤（如 <b>、<a>）依據語意位置重新插入翻譯後的節點內。

- **Pass 6: HTML 重組 (HTML Restoration)**  
  將完全翻譯並標記的節點合併回原始 HTML 骨架中，重建最終完美的 HTML 檔案。

## 4. 鳴謝

本專案由 **Gemini**、**ChatGPT** 與 **Grok** 協力開發探討。  
此次迭代的程式碼主撰寫者與架構師為 **Gemini 3.1 pro**。

---

## 5. 快速開始 (Quick Start) 與跨平台編譯

關於詳細的 CLI 指令用法、進階設定與 Prompt 微調指南，請參閱[**進階使用文件**](docs/AdvExp_zhtw.md)。

[**本指南**](docs/Cross-Platform_Building_Guide.md) 提供 Windows、Linux 和 macOS（Apple Silicon）的編譯命令和環境設定。
