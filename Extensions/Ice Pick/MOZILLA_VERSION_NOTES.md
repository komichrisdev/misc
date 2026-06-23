# Mozilla Version Notes

Suggested text for the Mozilla submission notes:

Ice Pick is a private DevTools helper intended for personal/internal use. It simplifies repetitive response checks in the browser Network tab during debugging and QA.

What this version does:

- Adds a custom DevTools panel for reviewing matching network responses.
- Lets the user create local rules that match requests by method, file name, path, or URL.
- Reads JSON response bodies locally and extracts configured fields for quick comparison.
- Stores rules and preferences locally only.
- Exports timestamped console logs and HAR files to the Downloads folder for debugging records.

Privacy and data handling:

- No analytics.
- No remote logging.
- No external network calls made by the extension.
- Captured data stays local to the browser and exported files are saved only when the user clicks Save Logs.

Scope and intended use:

- Intended for personal/private/internal debugging workflows.
- Used to reduce repetitive manual checking of API responses in DevTools.
- Only active while the DevTools panel is open.

Source/build note:

- Full step-by-step build instructions and environment requirements are included in `README.md`.
- The submitted source is plain readable JavaScript/HTML/CSS with no minification or transpilation.
