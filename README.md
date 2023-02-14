<div align="center">
  <a href="https://git-report.com">
	  <img width="150" src="https://raw.githubusercontent.com/adrien-nf/git-report-web/master/public/logo192.png" alt="GitReport">
  </a>
	</br>
  <h1> Git Report Api </h1>
  Api to get all the commits on a dev environment through a shell script and Event Stream
 
</div>

<hr>

This is the back-end repository. 👀 You may want to visit the [front-end repository](https://github.com/adrien-nf/git-report-web).

See `Program.cs` for all routes.

See `get-commits.sh` and `get-local-commits.sh` for the commits script.



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
