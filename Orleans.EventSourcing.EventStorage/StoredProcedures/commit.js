function commit(commitDoc) {
    var collection = getContext().getCollection();
    var collectionLink = collection.getSelfLink();
    var response = getContext().getResponse();

    if (!commitDoc) throw new Error("commitDoc is required");

    commitDoc = JSON.parse(commitDoc);

    var masterCommitQuery = 'SELECT * FROM c WHERE c.id = "0" AND c.partitionId = "commit"'
    var masterCommitQueryAccepted = collection.queryDocuments(collectionLink, masterCommitQuery, {}, function (err, masterCommitDocs, responseOptions) {

        if (err) throw new Error("ERROR OCCURRED", err);

        if (masterCommitDocs.length === 0) {
            throw new Error("master commit document not found");
        }

        var masterCommitDoc = masterCommitDocs[0];

        masterCommitDoc.number++;

        commitDoc.number = masterCommitDoc.number;
        commitDoc.id = masterCommitDoc.number.toString();

        collection.createDocument(collectionLink, commitDoc, {}, function (err, updatedCommitDoc, responseOptions) {

            if (err) throw err;

            collection.replaceDocument(masterCommitDoc._self, masterCommitDoc, { etag: commitDoc._etag }, function (err, updatedMasterCommitDoc, responseOptions) {

                if (err) throw err;

                response.setBody(JSON.stringify(updatedCommitDoc));

            })
        })
    });
}