# Help System

The `HelpSystem` class provides an interactive help shell for Olive Petrel.

## Overview

The help subsystem has been extracted from `Program.cs` into its own class (`HelpSystem.cs`) to improve code organization and maintainability.

## Features

- **Interactive Shell**: Browse help topics with a dedicated shell interface
- **Smart Shortcuts**: Automatically generates short aliases for quick topic access
- **Search**: Full-text search across all help topics
- **Recent History**: Tracks recently viewed topics for easy navigation
- **Fuzzy Matching**: Suggests similar topics when exact matches aren't found
- **Random Discovery**: Randomly pick a topic to explore

## Usage

```csharp
var helpSystem = new HelpSystem("docs/help");
helpSystem.StartHelpShell();
```

## Commands

Within the help shell, users can:

- Type a topic name or shortcut to view it
- `list` - Show all available topics with previews
- `search <term>` - Search for topics containing a keyword
- `random` - Display a random topic
- `recent` - Show recently viewed topics
- `menu` - Display the command menu
- `refresh` - Reload topics from disk
- `exit` - Return to the main emulator shell

## Help File Format

Help files are plain text files stored in the `docs/help/` directory:

- First non-empty line becomes the topic title
- Content is displayed as-is
- File name (without extension) is used for topic identification
- First meaningful line after title is used as preview text in listings

## Architecture

The `HelpSystem` class encapsulates:
- Topic loading and parsing
- Shortcut generation
- Search functionality
- Navigation history
- Fuzzy matching using Levenshtein distance
- Interactive shell loop

All help-related logic is now self-contained, making it easier to test, maintain, and potentially reuse in other projects.
