# Terrain Spline Editor

A lightweight, node-based spline tool for Unity to easily paint terrain textures and modify terrain height. Perfect for creating roads, paths, rivers, and flat areas on your Unity Terrains.

## Features

- **Path Mode**: Draw open paths (like roads or rivers) with a customizable width and falloff.
- **Shape Mode**: Draw closed shapes to level out areas (like building foundations or lakes).
- **Height Modification**: Non-destructively alter the terrain height to match your splines.
- **Texture Painting**: Automatically paint terrain layers along your splines.
- **Modern UI**: Intuitive Inspector and native Scene View Overlay integration.
- **Live Preview**: See your changes in real-time before applying them to the terrain data.
- **Undo/Redo Support**: Full support for Unity's Undo system.

## Installation

### Method 1: Install via Git URL (Recommended)

You can install this package directly through the Unity Package Manager using the Git URL.

1. Open Unity and go to **Window > Package Manager**.
2. Click the **+** button in the top left corner and select **Add package from git URL...**
3. Paste the following URL and append `?path=/Assets/TerrainSplinePlugin`:
   
   ```
   https://github.com/Exop63/TerrainSpline.git?path=/Assets/TerrainSplinePlugin
   ```
   
4. Click **Add**. Unity will download and install the package.

### Method 2: Local Package

1. Clone or download this repository.
2. Open Unity and go to **Window > Package Manager**.
3. Click the **+** button and select **Add package from disk...**
4. Navigate to the downloaded repository, go into the `Assets/TerrainSplinePlugin` folder, and select the `package.json` file.

## Usage

1. Go to **Tools > Terrain Spline > Create New Spline**. This will create a new Spline asset and open the main tool window.
2. Select the terrain you want to modify in the **Target Terrain** field.
3. Use the Scene View to draw your spline:
   - **Ctrl + Click**: Add a new node.
   - **Delete**: Remove the selected node.
   - **Shift + Click** on a segment: Insert a node between two existing nodes.
   - **Ctrl + Drag** a node: Snap to the terrain surface.
4. Use the **Scene Overlay Panel** (bottom left) to rename your spline, change its mode (Path or Shape), and adjust handle types (Free, Aligned, Mirrored).
5. In the **Terrain Spline Inspector**, configure your Brush Settings (Width, Falloff), Height Settings, and Paint Settings.
6. Toggle **Preview** in the Overlay or Inspector to see how the terrain will look.
7. Once satisfied, click **Apply To Terrain** in the Inspector to finalize your changes.

## Controls / Shortcuts

- **Q, W, E, R**: Switch between native Unity transform tools to move, rotate, or scale nodes.
- **Tab**: Cycle selection through nodes.
- **F**: Frame the currently selected node in the scene view.

## Requirements

- Unity 2022.3 or newer.
- A Unity Terrain in your scene.
