import { EntityStore, StoreActivityToken } from "../EntityStore";
import * as AS from "../AS";
import { RenderHost } from "../RenderHost";
import { IComponent } from "../IComponent";

export class Form implements IComponent {
    private sendTo: string;

    constructor(public host: RenderHost, private entityStore: EntityStore, public element: HTMLElement) {
        this.sendTo = element.dataset.endpoint;
        element.addEventListener("submit", (e) => this.submit(e));
    }

    private submit(e: Event) {
        e.preventDefault();

        let formData = new FormData(this.element as HTMLFormElement);
        let resultJson: {[a: string]: string|string[]} = {"@context": "https://www.w3.org/ns/activitystreams"};
        for (let item of (formData as any).keys()) {
            resultJson[item] = formData.getAll(item) as string[];
        }

        let headers = new Headers();
        headers.append("Content-Type", 'application/ld+json; profile="https://www.w3.org/ns/activitystreams"');

        this.entityStore.session.authFetch(this.sendTo, {
            body: JSON.stringify(resultJson),
            method: 'POST',
            headers
        }).then(async (a) => {
            if (a.status == 201) {
                window.history.pushState({}, document.title, a.headers.get("Location"));
                window.dispatchEvent(new PopStateEvent('popstate', {state: {}}));
            } else {
                let data = await a.text();
                console.log(data);
                alert(data);
            }
        })
        return false;
    }

    unbind() {

    }
}