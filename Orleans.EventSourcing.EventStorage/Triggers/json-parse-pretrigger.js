function jsonParse() {
    var context = getContext();
    var request = context.getRequest();

    var document = request.getBody();

    if (typeof document === 'string' || document instanceof String) {
        request.setBody(JSON.parse(document));
    } else {
        request.setBody(document);
    }    
}