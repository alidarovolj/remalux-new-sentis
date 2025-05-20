# AR Plane Persistence System

This system provides persistent AR planes similar to how Dulux Visualizer works, allowing planes to remain even when they're no longer visible to the camera.

## Overview

The AR Plane Persistence system consists of several components working together:

1. **ARManagerInitializer2**: Extended to support persistent planes
2. **ARPlaneConfigurator**: Contains the core persistence logic
3. **ARPlanePersistenceUI**: Provides user interface for saving/resetting planes
4. **ARPlanePersistenceUIBuilder**: Runtime tool to create UI elements

## How It Works

### Plane Persistence Logic

- When detecting planes, the system filters them to avoid duplicates or overlaps with existing persistent planes
- Planes can be marked as persistent which prevents them from being removed when they go out of view
- Persistent planes are visually distinguished from regular planes with a different color
- The number of persistent planes is tracked and displayed to the user
- Planes can be made persistent either all at once or individually by tapping on them

### Key Features

- **Plane Stability**: Persistent planes remain in the scene even when not visible to the AR camera
- **Tap-to-Save**: Users can tap on any plane to make it persistent
- **Save All Button**: Makes all currently visible planes persistent at once
- **Reset Button**: Clears all persistent planes and starts fresh
- **Visual Feedback**: Different colors for regular vs. persistent planes

## Setup Instructions

### Using the Editor Tool

1. Open the Unity editor
2. Go to "AR Tools > Setup Plane Persistence" in the menu
3. Select your ARManagerInitializer2 and ARPlaneConfigurator components
4. Choose whether to create a UI
5. Click "Setup Plane Persistence"

### Manual Setup

1. Make sure ARManagerInitializer2 has `usePersistentPlanes` and `highlightPersistentPlanes` set to true
2. Create a UI canvas with:
   - A "Save Planes" button
   - A "Reset Planes" button
   - A status text display
3. Add the ARPlanePersistenceUI component and assign references to:
   - ARManagerInitializer2
   - ARPlaneConfigurator
   - UI buttons and text elements

### Runtime UI Creation

You can also use the ARPlanePersistenceUIBuilder component to create UI elements at runtime:

```csharp
var builder = gameObject.AddComponent<ARPlanePersistenceUIBuilder>();
builder.arManagerInitializer = FindObjectOfType<ARManagerInitializer2>();
builder.planeConfigurator = FindObjectOfType<ARPlaneConfigurator>();
builder.BuildUI();
```

## Usage

- **To Save Individual Planes**: Tap on a plane
- **To Save All Planes**: Press the "Save Planes" button
- **To Reset All Planes**: Press the "Reset Planes" button

## Technical Notes

- The system uses a combination of filtering and tracking to manage persistent planes
- New planes that would overlap with existing persistent planes are filtered out
- AR planes created by ARManagerInitializer2 are named with the pattern "MyARPlane_Debug_*"
- The integration touches two primary systems:
  1. The AR Foundation plane detection and management system
  2. The custom plane creation system in ARManagerInitializer2 