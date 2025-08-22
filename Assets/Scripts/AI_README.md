# Satie AI Code Generation

## Setup Instructions

1. **Get an OpenAI API Key**
   - Go to https://platform.openai.com/api-keys
   - Create a new API key
   - Copy the key (starts with `sk-`)

2. **Add API Key to Unity**
   - Create a new file: `Assets/api_key.txt`
   - Paste your API key into this file
   - Save the file
   - ⚠️ IMPORTANT: This file is already in .gitignore - never commit API keys!

3. **Using the AI Generator**
   - Select any GameObject with a `SatieRuntime` component
   - In the Inspector, find the "AI Code Generation" section
   - Enter a natural language prompt describing the audio experience you want
   - Click "Generate Satie Code"
   - Review the generated code
   - Click "Apply to Current Script" or "Save as New Script"

## Example Prompts

- "Create a peaceful forest ambience with birds chirping randomly"
- "Make a busy street scene with cars passing by and footsteps"
- "Generate spooky atmosphere with random voices and ambient sounds"
- "Create rhythmic beat with multiple synchronized loops"
- "Design a 3D audio experience with sounds moving around the listener"

## Features

- Uses OpenAI's o1-preview model for advanced reasoning
- Understands all Satie language syntax
- Knows about available audio resources in your project
- Generates clean, commented code
- Can create complex multi-layered soundscapes
- Supports spatial audio with movement patterns

## Troubleshooting

- **API Key Not Found**: Make sure `Assets/api_key.txt` exists and contains your key
- **API Request Failed**: Check your internet connection and API key validity
- **No Response**: Try a simpler prompt or check Unity console for errors

## Configuration

You can modify the AI settings in the `SatieAICodeGen.cs` file:
- `model`: Change to "gpt-4" or "gpt-3.5-turbo" for different models
- `temperature`: Adjust creativity (0.0 = deterministic, 1.0 = creative)
- `maxTokens`: Maximum length of generated code

## Security Note

Never share or commit your API key! The `.gitignore` file is configured to exclude:
- `/Assets/api_key.txt`
- `/Assets/*.key`
- Any file with `.key` extension in Assets folder