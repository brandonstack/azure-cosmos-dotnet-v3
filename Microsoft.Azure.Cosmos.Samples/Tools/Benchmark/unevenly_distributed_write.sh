#!/bin/bash

if [ -z "$ACCOUNT_ENDPOINT" ]
then
    echo "Missing ACCOUNT_ENDPOINT"
    exit -1
fi

if [ -z "$ACCOUNT_KEY" ]
then
    echo "Missing ACCOUNT_KEY"
    exit -1
fi

if [ -z "$RESULTS_PK" ]
then
    echo "Missing RESULTS_PK"
    exit -1
fi

if [ -z "$PL" ]
then
    echo "Missing PL"
    exit -1
fi

COMMIT_ID=$(git log -1 | head -n 1 | cut -d ' ' -f 2)
COMMIT_DATE=$(git log -1 --date=format:'%Y-%m-%d %H:%M:%S' | grep Date | cut -f 2- -d ':' | sed 's/^[ \t]*//;s/[ \t]*$//' | cut -f 1 -d ' ')
COMMIT_TIME=$(git log -1 --date=format:'%Y-%m-%d %H:%M:%S' | grep Date | cut -f 2- -d ':' | sed 's/^[ \t]*//;s/[ \t]*$//' | cut -f 2 -d ' ')
BRANCH_NAME=$(git rev-parse --abbrev-ref HEAD)

echo $COMMIT_ID
echo $COMMIT_DATE
echo $COMMIT_TIME
echo $BRANCH_NAME

#Point read operations
for WORKLOAD_NAME in Insert InsertSinglePk
do
    dotnet run -c Release  -- -n 200000 -w Insert --pl $PL --tcp 10 -e $ACCOUNT_ENDPOINT -k $ACCOUNT_KEY --disablecoresdklogging --database testdb --container testcol -t 10000 --partitionkeypath /pk
    echo "Data was prepared, start testing..."
    sleep 10
    now=$(date +"%T")
    echo "$WORKLOAD_NAME start at $now"
    dotnet run -c Release  -- -n 100000 -w $WORKLOAD_NAME --pl $PL --tcp 10 -e $ACCOUNT_ENDPOINT -k $ACCOUNT_KEY  --enablelatencypercentiles --disablecoresdklogging --publishresults --resultspartitionkeyvalue $RESULTS_PK --commitid $COMMIT_ID --commitdate $COMMIT_DATE --committime $COMMIT_TIME  --branchname $BRANCH_NAME --database testdb --container testcol -t 10000 --partitionkeypath /pk --cleanuponfinish
    now=$(date +"%T")
    echo "$WORKLOAD_NAME end at $now"
    sleep 10 #Wait
done
