# 🚀 快速開始 (Quick Start)

### 最基礎：產生與配置設定檔

在開始翻譯之前，您需要產生一份 YAML 格式的設定檔。請在終端機輸入：

HtmTxTrans --sc > config.yaml

(提示：如果您使用 Linux/Mac 或 PowerShell，也可以使用 HtmTxTrans --sc | tee config.yaml 同時輸出在螢幕與檔案中。)

用任何文字編輯器打開 config.yaml，裡面有詳細的參數說明。**您最少必須填寫以下四個欄位：**

1. ApiEndpoint (LLM API 網址)

2. ApiKey (API 金鑰，若本地模型不需要也可隨意填寫)

3. ModelName (LLM 模型名稱)

4. TargetLanguage (翻譯目標語言，如 Traditional Chinese)

💡 **強烈建議：設定 ContentDomain**  
告訴 LLM 這份文件的「文體與領域」，能大幅提升翻譯品質。建議使用 3~5 個標籤（Tags）來描述。

> **範例：** 假設您要翻譯一篇關於異世界轉生的日本輕小說，您可以這樣寫：  
> ContentDomain: "Japanese light novels, Reincarnation, Isekai"

設定完成後，您可以根據需求選擇以下兩種使用方式：

### 第一種：極限懶人模式 (One-Click Mode)

不想管複雜的流程？一行指令搞定一切：

HtmTxTrans -c config.yaml -i input.html

程式會自動幫您跑完 Pass 1 到 Pass 6。完成後，翻譯好的 HTML 會輸出在：  
input.working\input.Translated.html

### 第二種：極限細節模式 (Granular Pipeline Mode)

如果您是進階使用者，想對每一個步驟進行極致的微調，您可以將 Pass 1 到 Pass 6 拆分開來獨立執行。這非常適合寫成批次腳本 (Batch Script)：

HtmTxTrans -c config1.yaml -p prompt1.yaml -i input.html -w output_dir --p1  
HtmTxTrans -c config2.yaml -p prompt2.yaml -i input.html -w output_dir --p2  
HtmTxTrans -c config3.yaml -p prompt3.yaml -i input.html -w output_dir --p3  
HtmTxTrans -c config4.yaml -p prompt4.yaml -i input.html -w output_dir --p4  
HtmTxTrans -c config4retry.yaml -p prompt4retry.yaml -i input.html -w output_dir --p4r  
HtmTxTrans -c config5.yaml -p prompt5.yaml -i input.html -w output_dir --p5  
HtmTxTrans -c config5retry.yaml -p prompt5retry.yaml -i input.html -w output_dir --p5r  
HtmTxTrans -c config6.yaml -p prompt6.yaml -i input.html -w output_dir --p6

透過這種方式，您可以為每一個 Pass **分別指派不同的 LLM 模型與自訂 Prompt**，達到成本與品質的最佳平衡。

(想了解更多 CLI 參數？請隨時輸入 HtmTxTrans 查看指令清單。)

---

## 🛠️ 進階使用 (Advanced Usage)

為了讓您少走彎路，以下是針對底層運作與 LLM 特性的實戰經驗分享：

### 1. 本地端大模型 (Local LLMs) 建議底線

如果您使用本地端模型，推理能力（Reasoning）是關鍵。建議的底線模型等級約為：Gemma-4-31B、GPT-oss-120B 等同級模型。若模型過小，在處理結構對齊時容易產生幻覺。  
建議搭配：SlidingWindowSize: 500（或更低）以減輕本地 VRAM 與模型的認知負擔。

### 2. 各 Pass 的模型負擔差異

- **Pass 6**：純粹的本機 C# 字串處理，完全不需要 LLM。

- **Pass 3**：負擔適中，但**直接決定翻譯的最終品質與文風**，建議使用具備高度文學或專業素養的模型。

- **Pass 4 & Pass 5**：越後面的 Pass 負擔越重！這兩個階段要求 LLM 進行嚴格的 YAML 陣列修改與標籤插值，極度考驗模型的邏輯與服從性，需要強大的模型來勝任。

### 3. 雲端 API 報錯處理

如果您對接的是雲端服務（如 OpenAI, Anthropic, Gemini），且遭遇 HTTP 400 Bad Request 錯誤，通常是因為該平台不支援進階參數。  
請將 config.yaml 中的 Seed 與 DisableCachePrompt 改回預設值（通常是 -1 與 0）。

### 4. 特殊技巧：逐句翻譯

如果您追求極致的逐句對齊翻譯，可以將 SlidingWindowSize 設為 10，並將 SlidingWindowHardScale 設置為 100。  
⚠️ **警告**：這會將 HTML 切碎成數百甚至數千個 Chunk，嚴重拖慢執行速度並造成天文數字的 Token 費用開銷，請謹慎使用。

### 5. 極致省錢大法 (Cost-Saving Retries)

既然 Pass 4 與 Pass 5 最容易失敗，您不需要用昂貴的雲端模型跑全程！  
您可以先用便宜的本地模型跑完大批量任務。對於失敗的區塊，系統會自動記錄在 Pass.failure.yaml 中。  
此時，您只需換上最強的雲端模型設定檔，並下達重試指令：

HtmTxTrans -c strong_cloud.yaml -i input.html --p4r  
HtmTxTrans -c strong_cloud.yaml -i input.html --p5r

系統就只會針對那幾個失敗的 Chunk 進行救援，將花費降到最低！

### 6. 工作目錄檔案解析 (Pipeline Files)

HtmTxTrans 的所有中繼狀態皆為人類可讀的 YAML，這意味著您可以在任何兩個 Pass 之間**自行撰寫程式介入處理**（例如：用另一個 LLM 進行二次校對）。

- **(Pass 1) 提取與合併**
  
  - NodeAll.yaml：從 HTML 抽出的純文字節點總表。
  
  - hold.yaml：被暫時剝離的 Inline HTML 標籤總表。
  
  - metadata.yaml：記錄專案元資料 (邊界 ID 等)。
  
  - Pass0.n.yaml：初步切塊的原始節點陣列。
  
  - Pass1.n.yaml：經 LLM 解析並處理 SEP 標籤後的連續文本。

- **(Pass 2) 詞彙表**
  
  - Pass2.glist.yaml：從全文中提煉並音譯的雙語統一詞彙表。

- **(Pass 3) 翻譯**
  
  - Pass3.n.yaml：包含原文與翻譯結果的對照檔。

- **(Pass 4) 節點對齊**
  
  - Pass4.n.yaml：翻譯文字被塞回 Node 陣列的結構檔。

- **(Pass 5) 標籤對齊**
  
  - Final.n.yaml：將 hold.yaml 的標籤完美插回翻譯文字中的最終節點檔。

- **(Pass 6) 重組**
  
  - 輸入.Translated.html：最終大功告成的 HTML 檔案！

---

## 💡 後話 (Epilogue)

傳統的翻譯腳本往往是一個巨大的「黑盒子」，把 HTML 丟進去，祈禱它吐出正常的結果，一旦失敗就只能重頭來過。

HtmTxTrans 的設計哲學是 **「狀態透明化 (State Transparency)」** 與 **「極致的模組化 (Modularity)」**。  
透過將流程切分為 6 個獨立的 Pass，並全面採用 YAML 儲存狀態，我們將控制權完全交還給開發者。您不僅可以隨時中斷、檢查、手動修改某個 YAML 檔案，甚至可以寫 Python/Node.js 腳本在中間安插您自己的工作流。

本專案的系統架構與邏輯由 **Gemini**、**ChatGPT** 與 **Grok** 協力開發探討。程式碼的主撰寫者與重構推手為 **Gemini 3.1 pro**。

歡迎來到「解構主義」的 HTML 翻譯新世界。祝您編譯愉快！