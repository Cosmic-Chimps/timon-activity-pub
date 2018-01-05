import { ASObject } from "./AS";

function getHost(url: string) {
    try {
        let data = new URL(url);
        return data.origin;
    } catch (e) {
        return null;
    }
}


export class Session {
    constructor() {

    }

    private _token: string;
    private _host: string;
    private _userId: string;

    private _proxyUrl: string;
    private _uploadMedia: string;
    private _outbox: string;
    private _user: ASObject;
    private _search: string;

    public get token() { return this._token; }
    public get user() { return this._user; }
    public get outbox() { return this._outbox; }
    public get search() { return this._search; }

    public async set(token: string, user: string): Promise<void> {
        this._token = token;
        this._userId = user;
        if (token == null) return;
        this._host = getHost(this._userId);

        this._user = await (await this.authFetch(user)).json();
        this._outbox = this._user["outbox"];
        if ("endpoints" in this._user) {
            const endpoints = this._user["endpoints"];
            if ("proxyUrl" in endpoints) this._proxyUrl = endpoints["proxyUrl"];
            if ("uploadMedia" in endpoints) this._uploadMedia = endpoints["uploadMedia"];
            if ("search" in endpoints) this._search = endpoints["search"];
        }
    }

    public authFetch(input: string | Request, init?: RequestInit): Promise<Response> {
        let request = new Request(input, init);
        if (this._token != null)
            request.headers.set("Authorization", "Bearer " + this._token);
        request.headers.set("Accept", "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/activity+json");
        return fetch(request);
    }

    public async getObject(url: string): Promise<ASObject> {
        const requestHost = getHost(url);
        if (requestHost != this._host && this._proxyUrl !== undefined) {
            const parms = new URLSearchParams();
            parms.append("id", url);
            let requestInit: RequestInit = {
                method: 'POST',
                body: parms
            };

            return await (await this.authFetch(this._proxyUrl, requestInit)).json();
        }

        return await (await this.authFetch(url)).json();
    }
}