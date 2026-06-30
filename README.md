# AI Video Auditor
<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/2d1a1a98-bb00-416f-960a-c60a2a3610d4" />

A real-time intelligent surveillance monitoring application built with C# and WinForms on .NET 10. The system processes video streams via OpenCV, runs edge object detection using YOLOv8, displays a live rolling graph of people counts, and automatically aggregates interval telemetry to fetch security insights from the Gemini AI API.

## Features
- **Real-Time Edge AI:** Utilizes YOLOv8-Nano for rapid pedestrian tracking.
- **Dynamic Charting:** Features a high-performance, real-time rolling ECG-style line graph powered by ScottPlot 5.
- **Draggable UI Layout:** Uses a modern WinForms split-container approach to prevent element overlapping and allow on-the-fly resizing.
- **LLM Security Auditing:** Automatically packages interval telemetry data into structured JSON packets and queries Gemini Flash for concise security risk assessments.

## Prerequisites
- **OS:** Windows 10/11
- **IDE:** Visual Studio 2022 (with .NET 10 SDK installed)
- **Hardware:** Works on CPU, but optimized for local GPUs (e.g., NVIDIA RTX series)

## Getting Started

1. **Clone the repository:**
   ```bash
   git clone [https://github.com/YOUR_USERNAME/AiVideoAuditor.git](https://github.com/YOUR_USERNAME/AiVideoAuditor.git)
   cd AiVideoAuditor
