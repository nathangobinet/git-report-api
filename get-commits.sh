#!/bin/sh

USERNAME=$(git config user.email)
FILE="./commits.csv"

rm $FILE 2>/dev/null && \
echo "🗑️ Deleted old $FILE"

echo "🔍 Searching for .git directories..."
find . -name ".git" | while read fname; do
  # Extract the repository name from the .git directory path
  REPO=$(echo $fname | rev | cut -d'/' -f 2 | rev)
  echo "📃 Processing commits of $REPO made by $USERNAME";
  # Write the commits to the temporary file
  git -C $fname --no-pager log --committer="$USERNAME" --pretty=format:"$REPO;%H;%ad;%s;%b;" >> $FILE
  echo "" >> $FILE
done

# Print the number of commits processed
COMMIT_COUNT=$(wc -l <$FILE)
echo "✅ Succesfully processed $COMMIT_COUNT commits"

echo "Do you agree to send the commits to our server for the browser to access? They will be deleted from our server once recovered"
read -p "Enter [Y/yes] to agree: " CHOICE

if [ "$CHOICE" = "y" -o "$CHOICE" = "Y" -o "$CHOICE" = "yes" -o "$CHOICE" = "Yes" -o "$CHOICE" = "YES" ]; then
  # Upload the file to the server
  curl -X POST -H "EventStreamId: {{ID}}"  -F "file=@$FILE" http://localhost:5058/commits && \
    rm $FILE && \
    echo "✅ Commits sent, go back to your navigator"
else
  echo "✅ Commits sored on in $FILE, please upload them manually"
fi