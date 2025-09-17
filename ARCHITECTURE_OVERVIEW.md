# SatieLang Modular Architecture

## Overview

SatieLang now uses a modular component-based architecture that separates concerns for better maintainability and extensibility.

## Core Components

### 1. SatieRuntime
**Purpose**: Runtime execution of .sp scripts with spatial audio support

**Responsibilities**:
- Parse and execute .sp script files
- Manage audio sources and spatial positioning
- Handle real-time audio playback and effects
- Steam Audio integration for HRTF
- Visual effects (trails, primitives, prefabs)

**Location**: `Assets/Scripts/SatieRuntime.cs`

### 2. SatieAICodeGen
**Purpose**: AI-powered code generation for .sp scripts

**Responsibilities**:
- OpenAI API integration for code generation
- Resource-aware prompting (scans available audio files)
- Conversation context management for follow-up edits
- RLHF (Reinforcement Learning from Human Feedback)
- Caching and performance optimization

**Location**: `Assets/Scripts/SatieAICodeGen.cs`

### 3. SatieAudioGen
**Purpose**: Multi-provider audio generation

**Responsibilities**:
- AudioLDM2 integration (local AI audio generation)
- Eleven Labs integration (cloud sound effects)
- Test mode (sine wave generation)
- Audio format conversion and caching
- Provider-specific parameter management

**Location**: `Assets/Scripts/SatieAudioGen.cs`

## Editor Architecture

### Modular Inspector System

Each component has its own dedicated editor with specialized functionality:

#### SatieRuntimeEditor
- Script file assignment and preview
- Spatial audio configuration
- Runtime controls (reload, reset)
- Component dependency management
- Play mode shortcuts

#### SatieAICodeGenEditor
- AI model configuration
- Prompt interface with edit mode
- Conversation history tracking
- RLHF feedback collection
- Generated code management

#### SatieAudioGenEditor
- Provider selection (AudioLDM2/ElevenLabs/Test)
- Provider-specific settings (duration, prompt influence, inference steps)
- Audio generation and preview
- Multi-option generation with selection
- Generated audio file management

### Component Dependency System

The `SatieRuntimeEditor` includes a component setup section that:
- Checks for presence of all required components
- Provides one-click setup to add missing components
- Shows status indicators for each component
- Guides users through proper configuration

## Usage Workflow

### Initial Setup
1. Create a GameObject with `SatieRuntime` component
2. Unity Inspector will show "Component Setup" section
3. Click "Add All Missing Components" to add AI and Audio generation
4. Each component now has its own inspector section

### AI Code Generation
1. Use `SatieAICodeGen` inspector section
2. Configure API key and model settings
3. Enter natural language prompt
4. Toggle edit mode for follow-up modifications
5. Apply generated code to runtime

### Audio Generation
1. Use `SatieAudioGen` inspector section
2. Choose provider (AudioLDM2 for experimental, ElevenLabs for professional)
3. Configure provider-specific settings via sliders
4. Generate multiple options and preview
5. Save selected audio to Resources folder

### Runtime Execution
1. Assign .sp script to `SatieRuntime`
2. Configure spatial audio settings
3. Use runtime controls for testing
4. Real-time reload with R/Shift+R shortcuts

## Benefits of Modular Architecture

### Separation of Concerns
- Runtime execution isolated from generation logic
- AI and audio generation are independent
- Each component can be developed/tested separately

### Extensibility
- Easy to add new audio providers
- AI models can be swapped without affecting other systems
- New generation methods can be added as separate components

### Maintainability
- Clear ownership of functionality
- Reduced coupling between systems
- Easier debugging and testing

### User Experience
- Specialized interfaces for different tasks
- Progressive disclosure of complexity
- Clear visual organization in Inspector

## Configuration Files

### audio_config.json
```json
{
  "default_provider": "audioldm2",
  "elevenlabs": {
    "api_key": "your_key_here",
    "duration_seconds": 10.0,
    "prompt_influence": 0.3
  },
  "audioldm2": {
    "model_id": "cvssp/audioldm2",
    "use_float16": true,
    "num_inference_steps": 200,
    "audio_length_in_s": 10.0
  }
}
```

### api_key.txt
Contains OpenAI API key for AI code generation

## Development Guidelines

### Adding New Features
1. Determine which component the feature belongs to
2. Add functionality to the component class
3. Update the corresponding editor if UI changes are needed
4. Maintain separation of concerns

### Component Communication
- Components communicate through Unity's standard patterns
- Use events/callbacks for loose coupling
- Avoid direct references between generation components

### Testing Strategy
- Each component can be tested independently
- Use dependency injection for external services
- Mock external APIs for unit testing

## Migration from Legacy Architecture

The old monolithic `SatieRuntimeEditor` has been replaced with the modular system. Key changes:

- **Before**: Single large editor with all functionality mixed together
- **After**: Three focused editors, each handling specific concerns
- **Migration**: Existing GameObjects will automatically detect missing components and offer to add them

This modular approach provides a solid foundation for future enhancements while maintaining clean separation of concerns and excellent user experience.