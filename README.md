<img width="310" alt="WideLogo620x300" src="https://github.com/user-attachments/assets/c1d9d95d-2ef1-4518-ab4c-64565dead0fd" />
<img width="1919" height="1195" alt="image" src="https://github.com/user-attachments/assets/48610d0a-b6d3-4453-af2c-70146fb949f1" />
  
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078d7.svg)](https://www.microsoft.com/windows)
  
[![Architecture](https://img.shields.io/badge/Arch-x64%20|%20ARM64-orange.svg)]()

Worlds most clean UI/UX multimedia player ever made..... ever

---

## ✨ Key Features

### Playback
*   **Codec Support:** Native playback for **AV1** and **HEVC**. *(Note: Codecs officially maintained by Microsoft and must be downloaded from the Microsoft Store.)*
*   **Precision Control:** Fully adjustable playback speed and real-time **Zoom In/Out** functionality.
*   **Subtitle Integration:** Automatically detects `.srt` files with matching names or allows for manual loading.
*   **Mirror Mode:** Project a mirror window of your current media to an external display or secondary monitor instantly.

### Audio & Internet Radio
*   **Backed by AudioGraph API:** Its a fine tuned api, what else can I say! 
*   **10-Bands Equalizer.**
*   **Internet Radio:** Built-in streaming support for web radio stations with huge list of radiostations from around the globe.
*   **Legacy Media:** Experimental support for **CD & DVD** playback.

### Live Wallpaper Audio Visualizer
*   **GPU Accellerated Audio Visualizer:** Super lightweight due to gpu utilization and will not slowing down your desktop experience. All thanks to Win2D 
*   **Desktop Integration:** Use any of the built-in visualizer presets as your fully animated **Active Live Wallpaper**.
*   **Preset Library:** A massive collection of visualizer presets included—ready to use out of the box.

### Trimming/Slicing OR Clip Tools
*   **Timeline Markers:** Set markers to slice or cut specific segments of your audio/video files.
*   **Export Options:** 
    *   Save specific segments via Timeline Markers.
    *   Export raw video frames as high-quality **JPEG** images.
    *   Support both audio and video trimming/slicing (formats depends on the installed codecs).

### Metadata 
*   Somewhat feature complete metadata viewer/editor
*   Able to pull the metadata from online database.


### Raw Frames (Bonus) 
*   It can export raw frames as JPEG (have fun!)
---

## 🛠 Technical Stack
*   WinUI & Win2D
*   Just clone and hit build on Visual Studio it will just work (**fingerCross*). 
*   vscode isn't supported due to WinUI.
*   Sorry, no XAML

## TODO
*   GStreamer version of this multimedia player is already in the work. Hopefully I'll have time to finish them (MFT is low latency but lack of codec support is all).

Available on Microsoft Store (*FREE*) -> [SSPlayer](https://apps.microsoft.com/detail/9p5qldhq3ljg?hl=en-US&gl=US)  
---

## 📦 Requirements
To enable hardware-accelerated decoding for modern formats, ensure you have the following installed from the Microsoft Store:
*   [AV1 Video Extension](https://www.microsoft.com/store/productId/9NCTDW2W1BH8)
*   [HEVC Video Extensions](https://www.microsoft.com/store/productId/9NMZLZ57R3T7)
*   OR it will be prompted on very 1st boot after fresh install. Feel free to opt-out if you want.
*   Windows 11 is a MUST for the backdrop & WinRT apis to work (might work on 10 but completely untested).
---
<img width="1919" height="1199" alt="image" src="https://github.com/user-attachments/assets/cd26a49b-1441-4584-93bf-5898a78ba649" />
<img width="1919" height="1199" alt="image" src="https://github.com/user-attachments/assets/070df220-f354-4e96-9aec-2c51ba33a3c6" />

## ⚖️ License

**MIT License**  
Copyright (c) 2026 **Breadnone**

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "SSPlayer"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

---
*I just want a clean uiux player is all*
