🧪 **X-LAB Git Publish Pack**

A small Windows BAT tool for publishing projects from a working folder to a clean GitHub folder.

```text
working folder → GitHub folder → commit → push → tag
```

It does not replace Git.
It just removes repetitive commands from my usual release workflow.



🤔 **Why**

I often keep two folders for a project:

```text
D:\projects\MP\...        # working folder
D:\projects\GitHub\...    # clean publish folder
```

The workflow is safe, but repetitive:

```text
copy files
check git status
commit
push
create tag
```

This pack automates that process while still asking for confirmation before important actions.



⚙️ **Scripts**

| Script                | Purpose                                                    |
| --------------------- | ---------------------------------------------------------- |
| `00_connect_repo.bat` | First-time local Git setup for an existing GitHub repo     |
| `01_sync.bat`         | Backup + sync working folder to GitHub folder              |
| `02_commit.bat`       | Show status + create commit                                |
| `03_push.bat`         | Push to GitHub                                             |
| `04_tag.bat`          | Create and push release tag                                |
| `99_full_publish.bat` | Full flow: backup → sync → status → commit → optional push |



🛠 **Setup**

Copy:

```text
config.example.bat
```

to:

```text
config.bat
```

Then edit:

```bat
set "PROJECT_NAME=YourProject"
set "SRC=D:\projects\MP\YourProject"
set "DST=D:\projects\GitHub\YourProject"
set "BACKUP_DIR=D:\projects\Backups\YourProject"
set "BRANCH=main"
set "REMOTE_URL=https://github.com/YourUsername/YourProject.git"
```

`config.bat` is local and should not be committed.



🚀 **First-time use**

Create an empty GitHub repository manually.

Then run:

```text
00_connect_repo.bat
```

This initializes the local Git folder, copies files, creates the first commit, connects `origin`, and pushes to GitHub.


🔁 **Normal use**

After working on the project, run:

```text
99_full_publish.bat
```

It will create a backup, sync files, show `git status`, ask for a commit message, commit changes, and optionally push.


🏷 **Release tag**

After publishing a release commit, run:

```text
04_tag.bat
```

Example:

```text
v1.0.0
```


📦 **Requirements**

* Windows
* Git
* PowerShell


📝 **Notes**

This tool is intentionally simple.
It is built for a separated workflow where development happens in one folder and publishing happens from another clean Git folder.

Part of **X-LAB** — practical scripts, tools, and automation utilities under the **XCON | RX** ecosystem.


📄 **License**

**MIT License**.
