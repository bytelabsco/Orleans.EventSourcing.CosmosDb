function bumpCommit() {
    var collection = getContext().getCollection();
    var collectionLink = collection.getSelfLink();
    var response = getContext().getResponse();

    var masterCommitQuery = 'SELECT * FROM c WHERE c.id = "0" AND c.partitionId = "commit"'
    var isAccepted = collection.queryDocuments(collectionLink, masterCommitQuery, {}, function (err, masterCommitDocs, responseOptions) {

        if (err) throw err;

        if (masterCommitDocs.length === 0) {
            throw new Error("master commit document not found");
        }

        var masterCommitDoc = masterCommitDocs[0];
        masterCommitDoc.number++;

        collection.replaceDocument(masterCommitDoc._self, masterCommitDoc, { etag: masterCommitDoc._etag }, function (err, updatedMasterCommitDoc, responseOptions) {

            if (err) throw err;

            response.setBody(updatedMasterCommitDoc.number);
        })
    });
}