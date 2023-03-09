# Figma Export

A tool to export Figma assets using the Figma API.
Intended usage is for Markdown files.
The front-matter is inspected and looks for the `figma` key.
The value is a URL to a Figma page, which will be used to export existing assets.

The expected structure of a Markdown file is:

```markdown
---
some-key: some-value
figma: https://www.figma.com/file/aaaaaaaaaaaaaaaaaaaaaa/?node-id=0%3A1
another-key: another-value
---

# Markdown

Content ...
```

Any existing key (besides `figma`) in the front-matter is ignored.
The Markdown file is not changed.
The content of the Markdown file is inspected and looks for images. Those are used to filter the exports defined in the Figma page.
Only the ones whose name matches are exported from Figma.

## Usage

The following arguments are available:

Argument | Expected Value | Required
---------|----------|---------
--input <path> | Specify a directory to be scanned recursively or a single file | Yes
--pattern <pattern> | A file name pattern to use (e.g. *.md). Only matching file names will be inspected. | No
--export <folder> | The folder to be created at the level where the inspected file is | Yes
--empty-export-folder | If used, all existing files will be deleted from the export folder defined with `--export`  | No
--api-token <token> | A Figma API personal access token | Yes
--ignore-duplicates | Ignore duplicated elements in a Figma page | No
--svg-visual-check-only | Compare SVG files visually instead | No
--max-updates | Maximum number of updates before exiting. Use 0 to denote all | No
--report | File to write the markdown report summary to | No
--report-append | Append to existing report file. Default is to NOT append | No

Sample usage:

```bash
./figma-export \
    --input my-folder-with-markdown-files/ \
    --pattern *.md \
    --export figma
    --api-token A-FIGMA-API-PERSONAL-ACCESS-TOKEN
```
