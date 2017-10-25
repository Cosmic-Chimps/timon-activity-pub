import { EntityStore } from "./EntityStore";
import { Session } from "./Session";

export class SessionObjects {
    public get navbar(): string { return "kroeg:navbar" }
    public get newNote(): string { return "kroeg:newnote" }

    constructor(private _store: EntityStore, private _session: Session) {
        this.regenerate();
    }

    private static _placeholders: string[] = ["newnote", "newarticle"];

    public regenerate() {
        let navbar: {id: string, loggedInAs?: string} = {
            id: "kroeg:navbar"
        };

        if (this._session.user != null) navbar.loggedInAs = this._session.user.id;

        this._store.internal("navbar", navbar);
        for (let item of SessionObjects._placeholders)
            this._store.internal(item, { id: "kroeg:" + item });
    }
}