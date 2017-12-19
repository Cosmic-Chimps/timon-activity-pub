import { Session } from "./Session";
import { ASObject } from "./AS";
import * as jsonld from "jsonld";


export type ChangeHandler = (oldValue: ASObject, newValue: ASObject) => void;

export class StoreActivityToken {
    public items: {[id: string]: ChangeHandler[]} = {};

    public addToHandler(id: string, handler: ChangeHandler) {
        if (!(id in this.items)) this.items[id] = [];
        this.items[id].push(handler);
    }
}

let _documentStore: {[url: string]: jsonld.DocumentObject} = {};
let _promiseDocumentStore: {[url: string]: Promise<jsonld.DocumentObject>} = {};

async function _get(url: string): Promise<jsonld.DocumentObject> {
    let headers = new Headers();
    headers.append("Accept", "application/ld+json");

    console.log("Fetching " + url);

    let result = await fetch(url, { headers });
    let json = await result.text();
    let doc = {documentUrl: url, document: json};
    _documentStore[url] = doc;
    return doc;
}

function loadDocument(url: string, callback: (err: Error | null, documentObject: jsonld.DocumentObject) => void) {
    if (url in _documentStore) {
        callback(null, _documentStore[url]);
    } else {
        if (!(url in _promiseDocumentStore))
            _promiseDocumentStore[url] = _get(url);
        _promiseDocumentStore[url].then(a => callback(null, a), a => callback(a, null));
    }
}

export class EntityStore {
    private _handlers: {[id: string]: ChangeHandler[]} = {};
    private _cache: {[id: string]: ASObject} = {};
    private _get: {[id: string]: Promise<ASObject>} = {};

    constructor(public session: Session) {
        if ("preload" in window) {
            let preload = (window as any).preload;
            for (let item in preload)
                this._addToCache(item, preload[item]);
        }
    }

    private _addToHandler(id: string, handler: ChangeHandler) {
        if (!(id in this._handlers)) this._handlers[id] = [];
        this._handlers[id].push(handler);
    }

    private _removeFromHandler(id: string, handler: ChangeHandler) {
        this._handlers[id].splice(this._handlers[id].indexOf(handler), 1);
    }
    
    public register(handlers: {[id: string]: ChangeHandler}, existing?: StoreActivityToken): StoreActivityToken {
        if (existing == null) existing = new StoreActivityToken();
        for (let id in handlers) {
            this._addToHandler(id, handlers[id]);
            existing.addToHandler(id, handlers[id]);
        }

        return existing;
    }

    public deregister(handler: StoreActivityToken) {
        for (let id in handler.items) {
            for (let item of handler.items[id])
                this._removeFromHandler(id, item);
        }

        handler.items = {};
    }

    private static _eq(a: any, b: any) {
        if (typeof a == "string" && typeof b == "string") return a == b || (a.startsWith("_:") && b.startsWith("_:"));
        return a == b;
    }

    private static _equals(a: ASObject, b: ASObject) {
        let prevKeys = Object.getOwnPropertyNames(a);
        let newKeys = Object.getOwnPropertyNames(b);
        if (prevKeys.length != newKeys.length)
            return false;

        for (let key of prevKeys) {
            if (newKeys.indexOf(key) == -1) return false;
            if (Array.isArray(a[key]) != Array.isArray(b[key])) return false;
            if (Array.isArray(a[key])) {
                if (a[key].length != b[key].length) return false;

                for (let i = 0; i < a[key].length; i++) {
                    if (!EntityStore._eq(a[key][i], b[key][i]) return false;
                }
            } else if (typeof a[key] == "object" && typeof b[key] == "object") {
                if (!EntityStore._equals(a[key], b[key])) return false;
            } else {
                if (!EntityStore._eq(a[key], b[key])) return false;
            }
        }

        return true;
    }

    private _addToCache(id: string, obj: ASObject) {
        let prev: ASObject = undefined
        if (id in this._cache)
            prev = this._cache[id];

        if (prev !== undefined && EntityStore._equals(prev, obj))
            return;

        this._cache[id] = obj;

        if (id in this._handlers)
            for (let handler of this._handlers[id])
                handler(prev, obj);

    }

    public internal(id: string, obj: ASObject) {
        this._addToCache("kroeg:" + id, obj);
        return "kroeg:" + id;
    }

    public async search(type: "emoji"|"actor", data: string): Promise<ASObject[]> {
        let response = await this.session.authFetch(`${this.session.search}?type=${type}&data=${encodeURIComponent(data)}`);
        let json = await response.json();
        for (let item of json) {
            this._cache[item.id] = item; // bypass cache because stupid reasons
        }
        return json;
    }

    public clear() {
        this._cache = {};
        for (let item in this._handlers) {
            if (item.startsWith("kroeg:")) continue;
            this._processGet(item);
        }
    }

    private async loadDocument(url: string, callback: (err: Error | null, documentObject: jsonld.DocumentObject) => void) {
        try {
            let response = await this.session.authFetch(url);
            let data = await response.json();
            callback(null, data);
        } catch (e) {
            callback(e, null);
        }
    }

    private async _processGet(id: string): Promise<ASObject> {
        let processor = new jsonld.JsonLdProcessor();
        
        let data = await this.session.getObject(id);
        let context = {"@context": ["https://www.w3.org/ns/activitystreams", window.location.origin + "/render/context"] };
        let flattened = await processor.flatten(data, context as any, { documentLoader: loadDocument, issuer: new jsonld.IdentifierIssuer("_:" + id + ":b") }) as any;
        console.log(flattened);

        for (let item of flattened["@graph"]) {
            this._addToCache(item["id"], item);
        } 

        delete this._get[id];
        if (!(id in this._cache)) return this._cache[data.id];
        return this._cache[id];
    }

    public get(id: string, cache: boolean = true): Promise<ASObject> {
        if (id in this._cache && cache)
            return Promise.resolve(this._cache[id]);

        if (id in this._get)
            return this._get[id];

        this._get[id] = this._processGet(id);

        return this._get[id];
    }
}