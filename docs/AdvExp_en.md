# 🚀 Quick Start

### The Basics: Generating and Editing the Config

Before translating, you need a YAML configuration file. Generate the template by running this in CLI: 

HtmTxTrans --sc > config.yaml

(Tip: On Linux/Mac or PowerShell, you can use HtmTxTrans --sc | tee config.yaml to print to the screen and save to the file simultaneously.)

Open config.yaml in any text editor. **You MUST configure at least these four fields:**

1. ApiEndpoint (The URL of your LLM API)

2. ApiKey (Your API key; can be any string if using a local mock server)

3. ModelName (The exact model identifier)

4. TargetLanguage (e.g., Traditional Chinese or Spanish)

💡 **Pro-Tip: Set the ContentDomain**  
Guiding the LLM with the genre and context dramatically improves translation quality. We recommend using 3 to 5 descriptive tags.

> **Example:** If you are translating a Japanese Isekai light novel, you could write:  
> ContentDomain: "Japanese light novels, Reincarnation, Isekai"

Once configured, choose between the two operational modes below:

### Method 1: One-Click Mode (The Lazy Way)

Don't want to micromanage? Run the entire pipeline with a single command:

HtmTxTrans -c config.yaml -i input.html

The program will automatically execute Pass 1 through Pass 6. Your final file will be located at:  
input.working\input.Translated.html

### Method 2: Insanity Granular Pipeline Mode (The Hacker Way)

For advanced users who want absolute control, you can execute each pass independently. This is perfect for batch scripting:

HtmTxTrans -c config1.yaml -p prompt1.yaml -i input.html -w output_dir --p1  
HtmTxTrans -c config2.yaml -p prompt2.yaml -i input.html -w output_dir --p2  
HtmTxTrans -c config3.yaml -p prompt3.yaml -i input.html -w output_dir --p3  
HtmTxTrans -c config4.yaml -p prompt4.yaml -i input.html -w output_dir --p4  
HtmTxTrans -c config4retry.yaml -p prompt4retry.yaml -i input.html -w output_dir --p4r  
HtmTxTrans -c config5.yaml -p prompt5.yaml -i input.html -w output_dir --p5  
HtmTxTrans -c config5retry.yaml -p prompt5retry.yaml -i input.html -w output_dir --p5r  
HtmTxTrans -c config6.yaml -p prompt6.yaml -i input.html -w output_dir --p6

By doing this, you can assign **different LLM models and custom Prompts** for each pass, optimizing for both cost and quality.

(For a full list of CLI arguments, simply run HtmTxTrans.)

---

## 🛠️ Advanced Usage

To help you avoid common pitfalls, here are some insights regarding LLM behavior and system limits:

### 1. Local LLM Hardware Baseline

If you are running local models, reasoning capability is crucial. The recommended baseline is roughly: Gemma-4-31B, GPT-oss-120B, or equivalent. Smaller models may hallucinate during strict YAML alignment tasks.  
Recommendation: Keep SlidingWindowSize: 500 (or lower) to prevent VRAM exhaustion.

### 2. Cognitive Load Across Passes

- **Pass 6**: Uses pure local C# logic. No LLM required.

- **Pass 3 (Translation)**: Moderate cognitive load, but directly dictates the **translation quality and tone**. Use your most literate model here.

- **Pass 4 & 5 (Alignment)**: These are the heaviest! They require the LLM to perform strict in-place YAML array updates and semantic tag interpolation. Highly obedient and logical models are required.

### 3. Handling Cloud API Errors

If you encounter HTTP 400 Bad Request errors with cloud providers (OpenAI, Anthropic, Gemini), they might not support advanced sampling parameters.  
Reset Seed and DisableCachePrompt to their defaults (-1 and 0 respectively) in your config.yaml.

### 4. Sentence-by-Sentence Translation

For hyper-granular alignment, you can set SlidingWindowSize to 10 and SlidingWindowHardScale to 100.  
⚠️ **WARNING:** This will fragment your HTML into hundreds/thousands of chunks. It will severely impact execution time and result in astronomical Token costs. Use with extreme caution.

### 5. The Frugal Architect (Cost-Saving Retries)

Since Pass 4 & 5 are the most prone to LLM formatting failures, you don't need to run the entire workflow on expensive models like GPT-4o!  
Run the bulk of your processing on cheap/local models. Any failed chunks will automatically be logged in Pass.failure.yaml.  
Then, switch to a **powerful cloud config** and run the retry commands:

HtmTxTrans -c strong_cloud.yaml -i input.html --p4r  
HtmTxTrans -c strong_cloud.yaml -i input.html --p5r

The system will only utilize the expensive API to rescue the failed chunks, minimizing your costs.

### 6. Pipeline File Structure

HtmTxTrans saves all intermediate states as human-readable YAML. This means you can **write your own scripts to intervene** between any two passes.

- **(Pass 1) Extraction & Resolution**
  
  - NodeAll.yaml: Master list of pure text nodes.
  
  - hold.yaml: Master list of stripped inline HTML tags.
  
  - metadata.yaml: Project metadata.
  
  - Pass0.n.yaml: Raw chunked node arrays.
  
  - Pass1.n.yaml: Continuous text resolved from Pass 0 via LLM.

- **(Pass 2) Glossary**
  
  - Pass2.glist.yaml: The unified bilingual glossary extracted from the text.

- **(Pass 3) Translation**
  
  - Pass3.n.yaml: Original vs. Translated text mapping.

- **(Pass 4) Node Alignment**
  
  - Pass4.n.yaml: Node arrays with translated text re-inserted.

- **(Pass 5) Tag Alignment**
  
  - Final.n.yaml: Translated nodes with hold.yaml tags perfectly re-interpolated.

- **(Pass 6) Restoration**
  
  - input.Translated.html: The final, glorious HTML output!

---

## 💡 Epilogue

Traditional HTML translation scripts are often massive "black boxes"—you throw HTML in, pray it doesn't break the tags, and if it fails, you start from scratch.

The design philosophy of HtmTxTrans is **State Transparency** and **Extreme Modularity**.  
By slicing the workflow into 6 isolated passes and utilizing YAML for state persistence, control is handed entirely back to the developer. You can pause the pipeline, inspect files, manually fix a hallucinated chunk, or inject your own custom Python/Node.js validation scripts mid-stream.

This project was co-developed with the assistance of **Gemini**, **ChatGPT**, and **Grok**. The principal code author and architect for this iteration is **Gemini 3.1 pro**.

Welcome to the deconstructed era of HTML translation. Happy compiling!
