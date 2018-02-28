function jsonStringify() {
    var context = getContext();
    var request = context.getRequest();

    var document = request.getBody();

    if (typeof document !== 'string' || !(document instanceof String)) {
        document = JSON.stringify(document);
    }

    request.setBody(document);
}