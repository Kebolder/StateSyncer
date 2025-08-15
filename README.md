# VRChat State Syncer

A Unity Editor tool for creating synchronized animator states for VRChat avatars. This tool automatically clones your animator states and creates a network of interconnected states that can be controlled remotely via VRChat parameters.

(Vrchat syncing sucks and making a spiderweb of a bunch of remote states sucks too!!!)


### This is a test for now. I need feedback on how it runs for other players. 

Contact me on Discord!
**@www.vrchat.com**
https://discord.gg/gAvSUqdnnB



## Features

- **Automatic State Cloning**: Creates remote copies of your animator states with customizable prefixes
- **Two Sync Modes**: 
  - **Int Mode**: Uses a single integer parameter for state control
  - **Binary Mode**: Uses multiple boolean parameters for efficient state encoding
- **State Detection**: Automatically detects states with numeric suffixes (e.g., "ToggleState 1", "ToggleState 2")
- **Manual State Assignment**: Assign custom numbers to states without numeric suffixes
- **Parameter Driver Integration**: Automatically adds VRChat Avatar Parameter Drivers to sync states
- **Sub-State Machine Packing**: Optionally organize cloned states into a sub-state machine

## Installation

#### *Will make a VPM later*

1. **Import the Package**: Drag the `VRCHATStateSyncer.unitypackage` file into your Unity project's `Assets` folder and import all contents
2. **Access the Tool**: Go to `Jax's Tools > VRChat State Syncer` in the Unity menu bar
3. **VRChat SDK Required**: Make sure you have VRChat SDK 3.0 installed in your project

## How It Works

### Overview
The tool creates a network of cloned states that can transition to each other based on parameter values. When a parameter matches a state's number, the animator transitions to that state.

### State Numbering System
- **Automatic Detection**: States with numeric suffixes (e.g., "ToggleState 1", "ToggleState 2") are automatically numbered
- **Manual Assignment**: For states without numbers, you can assign custom numbers using the input fields
- **Binary Encoding**: In binary mode, state numbers are encoded across multiple boolean parameters

### Sync Modes

#### Int Mode
- Uses a single integer parameter (e.g., "SyncInt")
- Simple and straightforward
- Good for small numbers of states
- Example: ToggleState1, ToggleState2, ToggleState3 controlled by parameter values 1, 2, 3

#### Binary Mode
- Uses multiple boolean parameters for efficient encoding (e.g., "SyncBool1", "SyncBool2", "SyncBool3")
- Supports up to 8 boolean parameters (256 states maximum)
- More efficient for larger state sets
- Example: 3 boolean parameters can encode 8 states (000, 001, 010, 011, 100, 101, 110, 111)

## Usage Instructions

### Step 1: Select Your Animator Controller
1. Open the VRChat State Syncer tool (`Jax's Tools > VRChat State Syncer`)
2. Select your animator controller from the dropdown
3. The tool will automatically detect all layers and parameters

### Step 2: Choose a Layer
1. Select the layer containing the states you want to sync
2. The tool will display all states in that layer
3. States with numeric suffixes are automatically detected and numbered

### Step 3: Configure States (Optional)
1. For states without numeric suffixes, you can assign custom numbers
2. Enter a number in the "Assign #" field for each state
3. You can change state numbers at any time by editing the input fields
4. The tool will use these numbers for the sync network

### Step 4: Choose Sync Mode

#### For Int Mode:
1. Uncheck "Binary Mode"
2. Select an integer parameter from the dropdown
3. Optionally change the state prefix (default: "Remote_")

#### For Binary Mode:
1. Check "Binary Mode"
2. Select the boolean parameters you want to use
3. The tool will show you how many states can be encoded
4. Ensure you have enough parameters for your state count

### Step 5: Configure Options
- **Remove parameter driver from remote**: Removes VRChat Avatar Parameter Drivers from cloned states
- **Add Parameter Driver for local sync**: Adds parameter drivers to original states for local synchronization. This will add a new parameter driver behaviour to each layer set up if you do not have one already and can detect when premade drivers with the sync parameters are using
- **Pack into StateMachine**: Organizes cloned states into a sub-state machine

### Step 6: Create the Network
1. Click "Create Interconnected Clone Network"
2. The tool will create cloned states and wire up all transitions
3. Success message will confirm the operation

## Examples

### Example 1: Simple Int Mode Setup
```
Original States: ToggleState1, ToggleState2, ToggleState3
Parameter: SyncInt (Int)
Result: Remote_ToggleState1, Remote_ToggleState2, Remote_ToggleState3
- When SyncInt = 1 → Remote_ToggleState1
- When SyncInt = 2 → Remote_ToggleState2  
- When SyncInt = 3 → Remote_ToggleState3
```

### Example 2: Binary Mode Setup
```
Original States: ToggleState1, ToggleState2, ToggleState3, ToggleState4
Parameters: SyncBool1 (Bool), SyncBool2 (Bool)
Result: 4 states encoded in 2 bits
- ToggleState1: SyncBool1=false, SyncBool2=false
- ToggleState2: SyncBool1=true, SyncBool2=false
- ToggleState3: SyncBool1=true, SyncBool2=true
- ToggleState4: SyncBool1=false, SyncBool2=true
```

## Parameter Driver Integration

The tool can automatically add VRChat Avatar Parameter Drivers to your states:

- **Original States**: When "Add Parameter Driver for local sync" is enabled, the tool adds parameter drivers to original states to sync them locally
- **Cloned States**: When "Remove parameter driver from remote" is disabled, cloned states keep their parameter drivers for remote sync


1. **State Naming**: Use numeric suffixes (e.g., "ToggleState1", "ToggleState2") for automatic detection
2. **Parameter Planning**: Create parameters like "SyncInt" for int mode or "SyncBool1", "SyncBool2", etc. for binary mode
3. **Binary Mode Efficiency**: Use binary mode for 4+ states to save parameter slots
4. **Testing**: Test your sync network in VRChat before finalizing
5. **Backup**: Always backup your animator controller before using the tool

## Troubleshooting

### Common Issues

**"No Int parameters available"**
- Create an integer parameter in your animator controller first

**"No numbered states found"**
- Ensure your states have numeric suffixes (e.g., "ToggleState 1", "ToggleState 2") or assign manual numbers

**"Too Many States" (Binary Mode)**
- Select more boolean parameters or reduce your state count

**Parameter Drivers Not Working**
- Ensure VRChat SDK 3.0 is properly installed