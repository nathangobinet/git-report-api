#!/bin/sh

USERNAME=$(git config user.email)
FILE="./commits.csv"

rm $FILE 2>/dev/null && \
  echo "❌ Deleted old $FILE"

echo "🔍 Searching for .git directories..."
find . -type d -name node_modules -prune -false -o -name ".git" | while read fname; do
  # Extract the repository name from the .git directory path
  REPO=$(echo $fname | awk -F/ '{print $(NF-1)}')
  echo "📃 Processing commits of $REPO made by $USERNAME";
  # Write the commits to the temporary file
  git -C $fname --no-pager log --committer="$USERNAME" --pretty=format:"$REPO;%H;%an;%ae;%aD;%s" >> $FILE
  echo "" >> $FILE
done

# Print the number of commits processed
COMMIT_COUNT=$(wc -l <$FILE | tr -d ' ')
echo "✅ Succesfully processed $COMMIT_COUNT commits"

echo "Do you agree to send the commits to our server so that the browser can access them? They will be immediately deleted from our server once retrieved"
read -p "Enter [Y/yes] to agree: " CHOICE

if [ "$CHOICE" = "y" -o "$CHOICE" = "Y" -o "$CHOICE" = "yes" -o "$CHOICE" = "Yes" -o "$CHOICE" = "YES" ]; then
  # Upload the file to the server
  curl -X POST -H "EventStreamId: {{ID}}"  -F "file=@$FILE" https://flash.vps.webdock.cloud/api/commits && \
    rm $FILE && \
    echo "✅ Commits sent, go back to your navigator"
else
  echo "✅ Commits sored on in $FILE, please upload them manually"
fi
