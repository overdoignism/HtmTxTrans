# HtmTxTrans

繁體中文版本請見 [README_zhtw.md](README_zhtw.md)

## 1. Purpose & Concept

**HtmTxTrans** is a powerful Command-Line Interface (CLI) tool designed for batch, full-text translation of HTML files using Large Language Models (LLMs).

Translating raw HTML with LLMs often leads to broken tags, lost attributes, or hallucinated structures. HtmTxTrans solves this by utilizing a "Disassembly-Translate-Reassembly" pipeline. It meticulously extracts text nodes and inline tags from the HTML, translates the plain text with sequential context awareness, and seamlessly aligns and reconstructs the translated text back into a perfectly valid HTML structure.

## 2. Tech Stack & License

- **Framework:** .NET 8

- **Core Libraries:**
  
  - OpenAI (2.10.0, Official .NET SDK for standardized LLM API interactions, MIT)
  
  - YamlDotNet (17.0.1, For highly readable configuration, prompts, and strictly-typed state management, MIT)
  
  - AngleSharp (1.4.0, For robust HTML DOM parsing and manipulation, MIT)

- **License:** Released under the MIT License.

## 3. Workflow (The 6 Passes)

HtmTxTrans processes files through a highly resilient 6-pass workflow. Each pass saves its state as human-readable YAML files, allowing for easy debugging, resumption, and independent retries.

- **Pass 1: HTML Extraction & Chunking**  
  Extracts text and target attributes using AngleSharp. Strips inline tags and splits the text into optimal chunks based on a sliding window and LLM sentence-boundary detection.

- **Pass 2: Proper Noun Extraction (Glossary Generation)**  
  Scans the text chunks to identify proper nouns suitable for transliteration, generating a rolling bilingual glossary to ensure terminology consistency.

- **Pass 3: Context-Aware Translation**  
  Translates the plain text chunks using the generated glossary, passing sequential preceding and succeeding context to the LLM for highly accurate localization.

- **Pass 4: Node Alignment**  
  Performs an in-place YAML update, mapping the translated plain text fragments back into their original Node ID structures.

- **Pass 5: Tag Alignment**  
  Re-inserts the stripped HTML inline tags back into the translated nodes at their correct semantic positions.

- **Pass 6: HTML Restoration**  
  Reconstructs the final HTML file by merging the fully translated and tagged nodes back into the original HTML skeleton.

## 4. Acknowledgments

This project was co-developed with the assistance of **Gemini**, **ChatGPT**, and **Grok**.  
The principal code author and architect for this iteration is **Gemini 3.1 pro**.

---

## 5. Quick Start and Cross-Platform Building Guide

For detailed CLI usage, advanced configuration, and prompt engineering instructions, please refer to the [**Detailed Documentation**](docs/AdvExp_en.md).

[**This guide**](docs/Cross-Platform_Building_Guide.md) provides the compilation commands and environment setup for Windows, Linux, and macOS (Apple Silicon).