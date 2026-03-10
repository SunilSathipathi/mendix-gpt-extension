# Mendix GPT Extension

Mendix GPT Extension is a Mendix Studio Pro extension that adds an AI chat panel for exploring app structure and assisting with microflow work inside the IDE.

## What It Does

- Opens a dockable chat experience inside Mendix Studio Pro
- Reads project context such as modules, entities, pages, microflows, associations, and enumerations
- Supports AI-assisted microflow creation, rename, replace, and activity edits
- Stores local configuration and conversation history for the extension
- Serves the UI from local web assets packaged with the extension

## Project Structure

```text
src/
  Extensions/    Mendix extension entry points and menu integrations
  ModelReaders/  Project context extraction from the Mendix model
  ModelWriters/  Microflow generation and transactional write support
  Models/        Configuration, DTOs, chat messages, and instructions
  Resources/     Embedded prompt and guidance resources
  Services/      Chat orchestration, configuration, history, and API services
  Tools/         AI tool implementations used by the chat workflow
  ViewModels/    WebView-facing view models
  WebAssets/     HTML, CSS, and JavaScript for the chat UI
```

## Requirements

- Mendix Studio Pro 10.24.x with extension development enabled
- .NET 8 SDK
- An Anthropic API key or an OpenAI API key configured through the extension settings

## Build

```powershell
dotnet build .\src -c Release
```

The extension output is generated under `src\bin\Release\net8.0-windows\`.

## Deploy

Use the deployment script to build and copy the extension into a Mendix project:

```powershell
.\deploy.ps1 -TargetProject "C:\Path\To\Your\MendixProject"
```

This copies the extension binaries, manifest, and web assets to `extensions\AideLite\` in the target project.

## Run In Studio Pro

Start Mendix Studio Pro with extension development enabled:

```text
"C:\Program Files\Mendix\10.24.0\modeler\studiopro.exe" --enable-extension-development
```

After deployment, restart Studio Pro or synchronize the app directory.

## Notes

- Configuration is managed in the extension services and models under `src\Services` and `src\Models`
- Frontend behavior for the chat panel lives in `src\WebAssets`
- The repository currently targets `net8.0-windows` and `Mendix.StudioPro.ExtensionsAPI` 10.23.0
- The extension supports both Anthropic Claude models and OpenAI GPT models

## License

MIT
