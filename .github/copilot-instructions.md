# GitHub Copilot Instructions for MiningCo. Alert Speaker (Continued)

## Mod Overview and Purpose

**Mod Name:** MiningCo. Alert Speaker (Continued)  
**Description:** The MiningCo. Alert Speaker is an update to the original mod created by Rikiki. This mod provides an advanced alert system that warns colonists of danger via sound and visual signals, enhancing the safety measures in your colony. It is designed to prevent colonists from being caught off guard during pirate raids, with additional visual cues for deaf colonists.

## Key Features and Systems

- **Alert Speaker Device:** The core component that provides auditory and visual alerts based on the danger level.
  - **No Danger:** No light or sound.
  - **Low Danger:** Yellow light with two loud sirens.
  - **High Danger:** Red rotating light with four alert sirens.
- **Colonists' Consciousness Bonus:** Colonists passing near the Alert Speaker receive a small bonus to consciousness.

## Coding Patterns and Conventions

1. **Naming Conventions:**
   - Use PascalCase for class and method names, such as `Building_AlertSpeaker`.
   - Use camelCase for method parameters and local variables.

2. **Comments and Documentation:**
   - Provide XML documentation comments for public methods and classes.
   - Use inline comments to explain complex logic.

3. **Error Handling:**
   - Use try-catch blocks to handle potential exceptions, and log errors for debugging.

## XML Integration

- The mod uses XML to define various elements, including:
  - **Hediff Definitions:** Defined in `Hediffs_Adrenaline.xml`, these include `HediffAdrenalineMediumDef` and `HediffAdrenalineSmallDef`.
  - **Research Projects:** Defined in `ResearchProjects_AlertSpeaker.xml`.
  - **Sound Definitions:** Managed in `Sounds_AlertSpeaker.xml`.
  - **Thing Definitions:** Contained in `Building_AlertSpeaker.xml`, defining the Alert Speaker object and its properties.

## Harmony Patching

- Consider using the Harmony library to safely patch RimWorld methods without modifying the original game code.
- Patches should be placed in a separate folder or namespace, e.g., `MiningCo.AlertSpeaker.Patches`.
- Ensure all patches are well documented and reversible.

## Suggestions for Copilot

- When generating C# code:
  - Ensure the use of appropriate Mod API methods for sound and light effects.
  - Suggest methods to dynamically determine the danger level and trigger alarms accordingly.
- When generating XML content:
  - Suggest definitions for new alerts or upgrades, maintaining compatibility with existing elements.
- For Harmony patches:
  - Propose patches for enhancing interactions without conflicting with other mods.

Following these instructions ensures that the MiningCo. Alert Speaker (Continued) mod remains consistent, efficient, and easily maintainable. If you encounter errors or issues, please use the Log Uploader and Discord channel for support.


This comprehensive `.github/copilot-instructions.md` file provides an overview and guidance on developing and maintaining the MiningCo. Alert Speaker mod. It includes key aspects like coding conventions and integration of XML and Harmony patches, ensuring a smooth development process.

## Project Solution Guidelines
- Relevant mod XML files are included as Solution Items under the solution folder named XML, these can be read and modified from within the solution.
- Use these in-solution XML files as the primary files for reference and modification.
- The `.github/copilot-instructions.md` file is included in the solution under the `.github` solution folder, so it should be read/modified from within the solution instead of using paths outside the solution. Update this file once only, as it and the parent-path solution reference point to the same file in this workspace.
- When making functional changes in this mod, ensure the documented features stay in sync with implementation; use the in-solution `.github` copy as the primary file.
- In the solution is also a project called Assembly-CSharp, containing a read-only version of the decompiled game source, for reference and debugging purposes.
- For any new documentation, update this copilot-instructions.md file rather than creating separate documentation files.


## Hard rules (must follow)
- Do NOT run commands that modify the repo (no git commit, git apply, dotnet format) unless explicitly asked.
- Prefer minimal reads: read only the smallest code region needed (around the suspicious lines).
- When mentioning SonarQube issues, automatically use the SonarQube MCP service to fetch and address issues instead of making inferred fixes without querying SonarQube first.
- When mentioning the rimworld log, automatically use the Rimworld MCP service to fetch the log.

