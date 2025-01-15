File Deduplication HardLink Tool
================================

Welcome to the **File Deduplication HardLink Tool**, a lightweight utility designed to optimize file storage by deduplicating files and creating hard links. This project aims to streamline the process of managing duplicate files with a focus on performance and ease of use.

**Disclaimer**: This project is currently in **beta**. Please test thoroughly before using in production environments.

* * * * *

Features
--------

-   **File Deduplication**: Identify and handle duplicate files to save disk space.

-   **Hard Link Management**: Create and manage hard links for optimized storage.

-   **File Inflation**: Break hard links when necessary.

-   **Flexible Options**: Includes options to handle read-only files, prevent marking files as read-only, and more.

-   **SHA256 Hashing**: Ensures accurate and secure duplicate detection.

-   **Command-Line Interface**: Simple and effective CLI for user input and logging.

Getting Started
---------------

1.  Clone the repository:

    ```
    git clone https://github.com/SunsetQuest/FileDeduplication.git
    ```

2.  Navigate to the project directory and build the solution using your preferred .NET build tool.

3.  Run the program using the command-line interface:

    ```
    dotnet run -- [options]
    ```

### Example Usage

Deduplicate files in a specific directory:

```
dotnet run -- -directory "C:\MyFiles"
```

Prevent marking files as read-only:

```
dotnet run -- -DoNotMarkReadOnly
```

For detailed options, use the `-help` flag:

```
dotnet run -- -help
```

* * * * *

Revision History
----------------

-   **1/13/2025 8:00 AM**: Introduced `FileInflateCommandLine` for breaking hard links. Enhanced `Deduper` with SHA256 hashing. Added CLI support in `Program.cs`.

-   **1/11/2025 5:00 PM**: Added `FileSystemTools` class for hard link operations.

-   **1/11/2025 8:00 AM**: Added logic to handle read-only files. Yielded detailed `DedupResult` for skipped files.

-   **1/10/2025 11:00 PM**: Introduced `DoNotMarkReadOnly` option in `Deduper` and CLI.

-   **1/10/2025 8:00 PM**: Initial commit of project files.

* * * * *

Contributing
------------

Contributions are welcome! Feel free to open issues or submit pull requests to improve the project.

* * * * *

License
-------

This project is licensed under the MIT License.

* * * * *

Acknowledgments
---------------

Created with assistance from **ChatGPT**, with a little help from **Ryan Scott**. 😊
