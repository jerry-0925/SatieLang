# Script Template System

## Overview
The Script Template System allows users to quickly load pre-made Satie compositions by using natural language requests. Instead of generating new code from scratch, the system detects specific keywords and loads complete `.sat` files.

## How It Works

1. **Template Registration**: Templates are registered in `ScriptTemplateAgent.RegisterTemplates()` with:
   - **ScriptPath**: Path to the .sat file
   - **Keywords**: Array of trigger words
   - **Description**: User-friendly description

2. **Request Detection**: When a user makes a request, the system checks if:
   - The prompt contains template keywords (e.g., "avant-garde", "experimental")
   - The request indicates they want a complete piece (e.g., "initial piece of", "template")

3. **Ultra-Fast Loading**: If a template matches, it's loaded directly from disk without any API calls
   - No validation needed
   - No generation needed
   - Instant response

## Currently Registered Templates

### TK.sat - "Avant-garde"
**Keywords**: `avant-garde`, `avant garde`, `experimental`, `contemporary`, `modern composition`, `abstract`, `atonal`

**Example prompts that trigger this template**:
- "initial piece of avant-garde music"
- "starting experimental composition"
- "template for contemporary music"
- "example of abstract music"

**What it contains**: A complex multi-layered experimental composition featuring:
- Evolving forest ambience
- Water droplet effects with spatial movement
- Sacred bells and monk chants with delay
- Playground ambience transition
- 10 randomized prepared piano samples (tk/)
- 5 piano samples with trails
- 5 prepared piano samples with spheres
- Progressive filter sweeps and modulation

## Adding New Templates

To add a new template, edit `SpecialistAgents.cs` in the `RegisterTemplates()` method:

```csharp
_templates["ambient"] = new TemplateDefinition
{
    ScriptPath = "Assets/Satie Scripts/Ambient.sat",
    Keywords = new[] { "ambient", "atmosphere", "relaxing", "calm", "meditation" },
    Description = "Peaceful ambient soundscape"
};
```

## Integration Points

- **SatieAgentOrchestrator.cs** (line 114-139): Template check happens before any AI validation
- **SpecialistAgents.cs** (line 365-463): ScriptTemplateAgent implementation
- **Performance**: Template loading typically completes in <10ms vs 2000-5000ms for AI generation

## Benefits

1. **Speed**: Instant loading vs multi-second AI generation
2. **Consistency**: Same high-quality composition every time
3. **Learning**: Users can study well-crafted examples
4. **Cost**: No API calls for template requests
5. **Reliability**: No risk of generation errors
