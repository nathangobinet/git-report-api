# Git Report API (alpha)

Api to get all the commits of a dev environment throught a shell script and Event Stream.

See `Program.cs` for all routes.

See `get-commits.sh` for the commits script.

Simple client example :

```ts
import Papa, { ParseRemoteConfig } from 'papaparse';

function getTrFromCommits(commits: string[][]) {
  return commits.reduce((acc, commit) => {
    return acc += `<tr>${commit.reduce((commitAcc, info) => {
      return commitAcc += `<td>${info}</td>`
    }, '')}</tr>`;
  }, '');
}

const shell = document.getElementById('shell');
const commits = document.getElementById('commits');
const eventSource = new EventSource('/api/see');

eventSource.addEventListener('init', (event) => {
  if (!shell || !commits) return;
  shell.innerText = `sh -c "$(curl -fsSL https://flash.vps.webdock.cloud/api/script/${event.lastEventId})"`;
  commits.innerHTML = '';
});

eventSource.addEventListener('commits-ready', (event) => {
  if (!commits) return;
  const id = event.lastEventId;
  const url = `/api/get-commits/${id}`;
  Papa.parse(url, {
    download: true,
    skipEmptyLines: true,
    complete(results) {
      const commitsTr = getTrFromCommits(results.data);
      commits.innerHTML = commitsTr;
    },
  } as ParseRemoteConfig);
});
```