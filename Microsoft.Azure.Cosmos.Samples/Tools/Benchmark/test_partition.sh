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

if [ -z "$INCLUDE_QUERY" ]
then
    echo "Missing INCLUDE_QUERY"
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
for WORKLOAD_NAME in ReadStreamBenchmarkOperation ReadStreamSinglePkBenchmarkOpertation
do
    dotnet run -c Release  -- -n 2000000 -w $WORKLOAD_NAME --pl 30 --tcp 10 -e $ACCOUNT_ENDPOINT -k $ACCOUNT_KEY  --enablelatencypercentiles --disablecoresdklogging --publishresults --resultspartitionkeyvalue $RESULTS_PK --commitid $COMMIT_ID --commitdate $COMMIT_DATE --committime $COMMIT_TIME  --branchname $BRANCH_NAME --database testdb --container testcol --partitionkeypath /pk --cleanuponstart
    sleep 10 #Wait
done

#Insert operations
dotnet run -c Release  -- -n 2000000 -w InsertBenchmarkOperation --pl 30 --tcp 1 -e $ACCOUNT_ENDPOINT -k $ACCOUNT_KEY  --enablelatencypercentiles --disablecoresdklogging --publishresults --resultspartitionkeyvalue $RESULTS_PK --commitid $COMMIT_ID --commitdate $COMMIT_DATE --committime $COMMIT_TIME  --branchname $BRANCH_NAME --database testdb --container testcol --partitionkeypath /pk --cleanuponstart
sleep 45 #Wait

dotnet run -c Release  -- -n 2000000 -w InsertSinglePkBenchmarkOperation --pl 30 --tcp 1 -e $ACCOUNT_ENDPOINT -k $ACCOUNT_KEY  --enablelatencypercentiles --disablecoresdklogging --publishresults --resultspartitionkeyvalue $RESULTS_PK --commitid $COMMIT_ID --commitdate $COMMIT_DATE --committime $COMMIT_TIME  --branchname $BRANCH_NAME --database testdb --container testcol --partitionkeypath /pk --cleanuponstart
sleep 45 #Wait

if [ "$INCLUDE_QUERY" = true ]
then
    #Query operations
    # n value is lowered to 200000 because queries are significantly slower. This prevents the runs from taking to long.
    # pl is 16 because 18 was casuing a small amount of thorrtles.
    for WORKLOAD_NAME in QueryStreamCrossPkBenchmarkOperation QueryStreamSinglePkBenchmarkOperation
    do
        dotnet run -c Release  -- -n 200000 -w $WORKLOAD_NAME --pl 16 --tcp 10 -e $ACCOUNT_ENDPOINT -k $ACCOUNT_KEY --enablelatencypercentiles --disablecoresdklogging --publishresults --resultspartitionkeyvalue $RESULTS_PK --commitid $COMMIT_ID --commitdate $COMMIT_DATE --committime $COMMIT_TIME  --branchname $BRANCH_NAME --database testdb --container testcol --partitionkeypath /pk --cleanuponstart
        sleep 10 #Wait
    done
fi