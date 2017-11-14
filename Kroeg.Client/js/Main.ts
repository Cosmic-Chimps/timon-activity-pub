import { Session } from "./Session";
import { TemplateService, TemplateRenderer } from "./TemplateService";
import { EntityStore } from "./EntityStore";
import { RenderHost } from "./RenderHost";
import { SessionObjects } from "./SessionObjects";
import { SubRenderhost } from "./Components/SubRenderhost";

let statusUpdate: HTMLSpanElement = document.getElementById("navbar-js-error");
document.addEventListener("error", (e) => {
    statusUpdate.innerText = e.error.toString();
})

export class Kroeg {
    private _container: RenderHost;
    private _navbar: RenderHost;

    private _session: Session;
    private _entityStore: EntityStore;
    private _templateRenderer: TemplateRenderer;
    private _sessionObjects: SessionObjects;

    private _parseHash(): {[name: string]: string} {
        let split = window.location.hash.substring(1).split('&');
        let kvp: {[a: string]: string} = {};
        for (let item of split) {
            let splitItem = item.split('=');
            kvp[splitItem[0]] = decodeURIComponent(splitItem[1]);
        }
        return kvp;
    }

    private _log(msg: string) {
        statusUpdate.innerText = msg;
    }

    private _isRemote(id: string) {
        return !id.startsWith(window.location.origin);
    }

    constructor() {
        if (window.location.hash.startsWith("#oauth"))
            this._finish_oauth();

        this._session = new Session();
        this._setup();
    }

    private _getLocation(): string {
        if ("id" in this._parseHash())
            return this._parseHash()["id"];
        return window.location.toString().split('#')[0];
    }


    private loggedIn: boolean = false;
    private async _setup() {
        if (window.localStorage.getItem("expires") != null)
            if (parseInt(window.localStorage.expires, 10) > +(new Date)) {
                this._log("Logging you in...")
                await this._session.set(window.localStorage.access_token, window.localStorage.id);
                this.loggedIn = true;
            }

        this._entityStore = new EntityStore(this._session);
        if (this.loggedIn) this._entityStore.clear();

        this._templateRenderer = new TemplateRenderer(new TemplateService(), this._entityStore);

        this._sessionObjects = new SessionObjects(this._entityStore, this._session);
        
        document.addEventListener("click", (e) => this._handleClick(e), true);
        window.addEventListener("popstate", (e) => this._update(this._getLocation()));
        this._log("Getting your templates...")
        await this._templateRenderer.prepare();

        let container = document.getElementsByClassName("container")[0] as HTMLElement;
        this._container = new RenderHost(this._templateRenderer, this._entityStore, this._getLocation(), "body", container, null, this._parseHash());

        let navbar = document.getElementsByClassName("navbar")[0] as HTMLElement;
        this._navbar = new RenderHost(this._templateRenderer, this._entityStore, this._sessionObjects.navbar, "navbar/bar", navbar);
    }

    private _finish_oauth() {
        this._log("Finishing up login...");
        let split = window.location.hash.substring(1).split('&');
        let kvp: {[a: string]: string} = {};
        for (let item of split) {
            let splitItem = item.split('=');
            kvp[splitItem[0]] = decodeURIComponent(splitItem[1]);
        }
        //[oauth2]&response_type=token&redirect_uri=http://localhost:5000/asdf/&state=asdf
        if ("access_token" in kvp && "state" in kvp && "expires_in" in kvp) {
            window.localStorage.setItem("id", kvp.state);
            window.localStorage.setItem("access_token", kvp.access_token);
            window.localStorage.setItem("expires", (+(new Date) + parseInt(kvp.expires_in) * 1000).toString());
        }
        window.history.replaceState({}, document.title, window.location.toString().replace(window.location.hash, ""));    
    }

    private _update(id: string) {
        this._container.data = this._parseHash();
        this._container.id = id;
    }

    private _handleClick(e: MouseEvent) {
        if (e.button != 0 || !this.loggedIn) return;
        let target = e.target as Element;
        while (target != null && target.tagName != "A") target = target.parentElement;
        if (target == null) return;

        let link = target as HTMLAnchorElement;
        if (link.href.length == 0 || link.href.startsWith("javascript:") || link.dataset["open"] == "ext") return;

        e.preventDefault();
        e.stopPropagation();
        let href = link.href.split('#')[0];
        let hash = link.href.split('#')[1];
        if (this._isRemote(href)) {
            href = window.location.toString().split('#')[0] + "#id=" + href + (hash ? ('&' + hash) : '');
        }
        let elem = link.parentElement;
        if (link.dataset["open"] == "renderhost")
            while (elem != null && !SubRenderhost.RenderhostMap.has(elem)) {
                elem = elem.parentElement;
            }

        console.log(elem);
        if (elem == null || link.dataset["open"] != "renderhost") {
            window.history.pushState({id: link.href.split('#')[0]}, document.title, href);
            this._update(link.href.split('#')[0]);
        } else {
            SubRenderhost.RenderhostMap.get(elem).navigate(link.href);
        }
    }


    public async login() {
        let id = prompt("Log in as?", window.location.toString());
        let data = await this._entityStore.get(id, false);
        let endpoints = await this._entityStore.get(data["endpoints"]);
        let oauthEndpoint = endpoints["oauthAuthorizationEndpoint"] as string;
        if (oauthEndpoint.indexOf("?") == -1) oauthEndpoint += "?"; else oauthEndpoint += "&";
        oauthEndpoint += `state=${encodeURIComponent(id)}&response_type=token&redirect_uri=${window.location.toString()}%23oauth=1`;
        window.location.assign(oauthEndpoint);
    }

    public logout() {
        window.localStorage.clear();
        this._navbar.deregister();
        this._entityStore.clear();
        this._sessionObjects.regenerate();

        this._navbar.id = this._sessionObjects.navbar;
    }


    public static instance = new Kroeg();
}

(window as any).Kroeg = Kroeg.instance;
